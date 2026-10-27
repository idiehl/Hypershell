using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.ProfileServer;

public sealed record ProvisionedProfileAccount(
    string AccountId,
    string DisplayName,
    string AccessKey,
    int CredentialVersion);

public sealed record ProfileAccountState(
    string AccountId,
    string DisplayName,
    bool IsDisabled,
    int CredentialVersion);

public sealed class ProfileAccountStore
{
    private const long MaximumAccountDocumentBytes = 1024 * 1024;
    private const int MaximumAccounts = 10_000;
    private const int DerivedKeyBytes = 32;
    private const int SaltBytes = 16;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static readonly byte[] DummySalt =
        SHA256.HashData("hypershell-profile-server-dummy-account"u8.ToArray())[..SaltBytes];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly int _hashIterations;
    private readonly string _accountsPath;

    public ProfileAccountStore(ProfileServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalized = options.Normalize();
        DataRoot = normalized.DataRoot;
        _hashIterations = normalized.CredentialHashIterations;
        _accountsPath = Path.Combine(DataRoot, "accounts.json");
    }

    public string DataRoot { get; }

    public async Task<ProvisionedProfileAccount> CreateAsync(
        string accountId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var normalizedId = ProfileServerIdentity.NormalizeAccountId(accountId);
        var normalizedDisplayName = ValidateDisplayName(displayName);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentAsync(cancellationToken);
            if (document.Accounts.Any(account => string.Equals(
                    account.AccountId,
                    normalizedId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Account '{normalizedId}' already exists.");
            }

            if (document.Accounts.Count >= MaximumAccounts)
            {
                throw new InvalidOperationException(
                    "The account registry has reached its configured limit.");
            }

            var accessKey = CreateAccessKey();
            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            var now = DateTimeOffset.UtcNow;
            var record = new StoredProfileAccount
            {
                AccountId = normalizedId,
                DisplayName = normalizedDisplayName,
                IsDisabled = false,
                CredentialVersion = 1,
                HashAlgorithm = "PBKDF2-SHA256",
                HashIterations = _hashIterations,
                AccessKeySalt = Convert.ToBase64String(salt),
                AccessKeyHash = Convert.ToBase64String(
                    DeriveKey(accessKey, salt, _hashIterations)),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            await SaveDocumentAsync(
                new StoredAccountDocument
                {
                    Accounts = document.Accounts
                        .Append(record)
                        .OrderBy(account => account.AccountId, StringComparer.Ordinal)
                        .ToList()
                },
                cancellationToken);
            return new ProvisionedProfileAccount(
                record.AccountId,
                record.DisplayName,
                accessKey,
                record.CredentialVersion);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ProvisionedProfileAccount> RotateAccessKeyAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var normalizedId = ProfileServerIdentity.NormalizeAccountId(accountId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentAsync(cancellationToken);
            var existing = document.Accounts.SingleOrDefault(account =>
                string.Equals(
                    account.AccountId,
                    normalizedId,
                    StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException(
                    $"Account '{normalizedId}' does not exist.");
            var accessKey = CreateAccessKey();
            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            var replacement = existing with
            {
                CredentialVersion = checked(existing.CredentialVersion + 1),
                HashIterations = _hashIterations,
                AccessKeySalt = Convert.ToBase64String(salt),
                AccessKeyHash = Convert.ToBase64String(
                    DeriveKey(accessKey, salt, _hashIterations)),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await SaveDocumentAsync(
                Replace(document, replacement),
                cancellationToken);
            return new ProvisionedProfileAccount(
                replacement.AccountId,
                replacement.DisplayName,
                accessKey,
                replacement.CredentialVersion);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ProfileAccountState> SetDisabledAsync(
        string accountId,
        bool disabled,
        CancellationToken cancellationToken = default)
    {
        var normalizedId = ProfileServerIdentity.NormalizeAccountId(accountId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentAsync(cancellationToken);
            var existing = document.Accounts.SingleOrDefault(account =>
                string.Equals(
                    account.AccountId,
                    normalizedId,
                    StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException(
                    $"Account '{normalizedId}' does not exist.");
            var replacement = existing with
            {
                IsDisabled = disabled,
                CredentialVersion = disabled
                    ? checked(existing.CredentialVersion + 1)
                    : existing.CredentialVersion,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await SaveDocumentAsync(
                Replace(document, replacement),
                cancellationToken);
            return ToState(replacement);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ProfileAccountState> RenameAsync(
        string accountId,
        string newAccountId,
        CancellationToken cancellationToken = default)
    {
        var normalizedId = ProfileServerIdentity.NormalizeAccountId(accountId);
        var normalizedNewId = ProfileServerIdentity.NormalizeAccountId(
            newAccountId);
        if (string.Equals(
                normalizedId,
                normalizedNewId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The replacement account ID must differ from the current account ID.",
                nameof(newAccountId));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentAsync(cancellationToken);
            var existing = document.Accounts.SingleOrDefault(account =>
                string.Equals(
                    account.AccountId,
                    normalizedId,
                    StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException(
                    $"Account '{normalizedId}' does not exist.");
            if (document.Accounts.Any(account => string.Equals(
                    account.AccountId,
                    normalizedNewId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Account '{normalizedNewId}' already exists.");
            }

            var replacement = existing with
            {
                AccountId = normalizedNewId,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await SaveDocumentAsync(
                new StoredAccountDocument
                {
                    Accounts = document.Accounts
                        .Select(account => string.Equals(
                            account.AccountId,
                            normalizedId,
                            StringComparison.OrdinalIgnoreCase)
                                ? replacement
                                : account)
                        .OrderBy(
                            account => account.AccountId,
                            StringComparer.Ordinal)
                        .ToList()
                },
                cancellationToken);
            return ToState(replacement);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ProfileAccountState>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentAsync(cancellationToken);
            return document.Accounts.Select(ToState).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ProfileAccountState?> VerifyAccessKeyAsync(
        string accountId,
        string accessKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedId = TryNormalizeAccountId(accountId);
        StoredProfileAccount? account = null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentAsync(cancellationToken);
            if (normalizedId is not null)
            {
                account = document.Accounts.SingleOrDefault(candidate =>
                    string.Equals(
                        candidate.AccountId,
                        normalizedId,
                        StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            _gate.Release();
        }

        var supplied = accessKey ?? string.Empty;
        var salt = account is null
            ? DummySalt
            : Convert.FromBase64String(account.AccessKeySalt);
        var iterations = account?.HashIterations ?? _hashIterations;
        var actual = DeriveKey(supplied, salt, iterations);
        var expected = account is null
            ? new byte[DerivedKeyBytes]
            : Convert.FromBase64String(account.AccessKeyHash);
        var matches = CryptographicOperations.FixedTimeEquals(
            actual,
            expected);
        return matches && account is { IsDisabled: false }
            ? ToState(account)
            : null;
    }

    public async Task<ProfileAccountState?> FindAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var normalizedId = TryNormalizeAccountId(accountId);
        if (normalizedId is null)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentAsync(cancellationToken);
            var account = document.Accounts.SingleOrDefault(candidate =>
                string.Equals(
                    candidate.AccountId,
                    normalizedId,
                    StringComparison.OrdinalIgnoreCase));
            return account is null ? null : ToState(account);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<StoredAccountDocument> LoadDocumentAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_accountsPath))
        {
            return new StoredAccountDocument();
        }

        var info = new FileInfo(_accountsPath);
        if (info.Length is <= 0 or > MaximumAccountDocumentBytes)
        {
            throw new InvalidDataException(
                "The account registry is empty or exceeds its size limit.");
        }

        var payload = await File.ReadAllBytesAsync(
            _accountsPath,
            cancellationToken);
        var document = JsonSerializer.Deserialize<StoredAccountDocument>(
                           payload,
                           JsonOptions)
                       ?? throw new InvalidDataException(
                           "The account registry was empty.");
        ValidateDocument(document);
        return document;
    }

    private async Task SaveDocumentAsync(
        StoredAccountDocument document,
        CancellationToken cancellationToken)
    {
        ValidateDocument(document);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            document,
            JsonOptions);
        if (payload.LongLength > MaximumAccountDocumentBytes)
        {
            throw new InvalidOperationException(
                "The account registry exceeds its size limit.");
        }

        await AtomicFile.WriteAsync(
            _accountsPath,
            payload,
            restrictToOwner: true,
            cancellationToken);
    }

    private static void ValidateDocument(StoredAccountDocument document)
    {
        if (document.SchemaVersion != StoredAccountDocument.CurrentSchemaVersion
            || document.Accounts.Count > MaximumAccounts
            || document.Accounts.GroupBy(
                    account => account.AccountId,
                    StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
        {
            throw new InvalidDataException(
                "The account registry schema, count, or identities are invalid.");
        }

        foreach (var account in document.Accounts)
        {
            _ = ProfileServerIdentity.NormalizeAccountId(account.AccountId);
            _ = ValidateDisplayName(account.DisplayName);
            if (account.CredentialVersion < 1
                || account.HashAlgorithm != "PBKDF2-SHA256"
                || account.HashIterations < 100_000
                || Convert.FromBase64String(account.AccessKeySalt).Length
                    != SaltBytes
                || Convert.FromBase64String(account.AccessKeyHash).Length
                    != DerivedKeyBytes
                || account.CreatedAtUtc == default
                || account.UpdatedAtUtc == default)
            {
                throw new InvalidDataException(
                    $"Account '{account.AccountId}' has invalid credential metadata.");
            }
        }
    }

    private static StoredAccountDocument Replace(
        StoredAccountDocument document,
        StoredProfileAccount replacement) =>
        new()
        {
            Accounts = document.Accounts
                .Select(account => string.Equals(
                    account.AccountId,
                    replacement.AccountId,
                    StringComparison.OrdinalIgnoreCase)
                        ? replacement
                        : account)
                .OrderBy(account => account.AccountId, StringComparer.Ordinal)
                .ToList()
        };

    private static ProfileAccountState ToState(StoredProfileAccount account) =>
        new(
            account.AccountId,
            account.DisplayName,
            account.IsDisabled,
            account.CredentialVersion);

    private static string CreateAccessKey() =>
        "hpk_" + Base64Url(RandomNumberGenerator.GetBytes(32));

    private static byte[] DeriveKey(
        string accessKey,
        byte[] salt,
        int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(accessKey),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            DerivedKeyBytes);

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string ValidateDisplayName(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > 120
            || normalized.Any(character => char.IsControl(character)))
        {
            throw new ArgumentException(
                "Account display names must contain 1-120 printable characters.",
                nameof(value));
        }

        return normalized;
    }

    private static string? TryNormalizeAccountId(string value)
    {
        try
        {
            return ProfileServerIdentity.NormalizeAccountId(value);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private sealed class StoredAccountDocument
    {
        public const int CurrentSchemaVersion = 1;

        [JsonRequired]
        public int SchemaVersion { get; init; } = CurrentSchemaVersion;

        [JsonRequired]
        public IReadOnlyList<StoredProfileAccount> Accounts { get; init; } = [];
    }

    private sealed record StoredProfileAccount
    {
        [JsonRequired]
        public string AccountId { get; init; } = string.Empty;

        [JsonRequired]
        public string DisplayName { get; init; } = string.Empty;

        [JsonRequired]
        public bool IsDisabled { get; init; }

        [JsonRequired]
        public int CredentialVersion { get; init; }

        [JsonRequired]
        public string HashAlgorithm { get; init; } = string.Empty;

        [JsonRequired]
        public int HashIterations { get; init; }

        [JsonRequired]
        public string AccessKeySalt { get; init; } = string.Empty;

        [JsonRequired]
        public string AccessKeyHash { get; init; } = string.Empty;

        [JsonRequired]
        public DateTimeOffset CreatedAtUtc { get; init; }

        [JsonRequired]
        public DateTimeOffset UpdatedAtUtc { get; init; }
    }
}

internal static class AtomicFile
{
    public static async Task WriteAsync(
        string path,
        ReadOnlyMemory<byte> payload,
        bool restrictToOwner,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new InvalidOperationException(
                            "The atomic destination has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous
                             | FileOptions.WriteThrough))
            {
                if (restrictToOwner && !OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(
                        temporaryPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }

                await stream.WriteAsync(payload, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
            if (restrictToOwner && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    fullPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
