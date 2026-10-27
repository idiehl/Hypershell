using Hypershell.Profiles;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.ProfileServer;

public sealed record StoredProfileFetch(
    RemoteProfileSummary Summary,
    ReadOnlyMemory<byte> Payload);

public enum ProfileStoreMutationKind
{
    Created,
    Updated,
    Deleted,
    Conflict,
    StorageLimit
}

public sealed record ProfileStoreMutation(
    ProfileStoreMutationKind Kind,
    RemoteProfileSummary? Profile = null);

public sealed class FileProfileStore
{
    private const string CurrentRecordFileName = "current.json";
    private const string CurrentPayloadPrefix = "current-";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        WriteIndented = true,
        MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _accountLocks =
        new(StringComparer.Ordinal);
    private readonly ServerPortableProfileValidator _validator;
    private readonly long _maximumAccountStorageBytes;
    private readonly string _profilesRoot;

    public FileProfileStore(
        ProfileServerOptions options,
        ServerPortableProfileValidator validator)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validator);
        var normalized = options.Normalize();
        _profilesRoot = Path.Combine(normalized.DataRoot, "profiles");
        _maximumAccountStorageBytes = normalized.MaximumAccountStorageBytes;
        _validator = validator;
    }

    public async Task<IReadOnlyList<RemoteProfileSummary>> ListAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var normalizedAccount = ProfileServerIdentity.NormalizeAccountId(accountId);
        var accountLock = GetAccountLock(normalizedAccount);
        await accountLock.WaitAsync(cancellationToken);
        try
        {
            var accountDirectory = GetAccountDirectory(normalizedAccount);
            if (!Directory.Exists(accountDirectory))
            {
                return [];
            }

            var recordPaths = Directory.EnumerateFiles(
                    accountDirectory,
                    CurrentRecordFileName,
                    SearchOption.AllDirectories)
                .Take(ProfileSyncContract.MaximumRemoteProfiles + 1)
                .ToList();
            if (recordPaths.Count > ProfileSyncContract.MaximumRemoteProfiles)
            {
                throw new InvalidDataException(
                    "The account contains more profile records than protocol v1 permits.");
            }

            var summaries = new List<RemoteProfileSummary>(recordPaths.Count);
            foreach (var recordPath in recordPaths)
            {
                var record = await ReadRecordAsync(
                    recordPath,
                    cancellationToken);
                record = await MigrateToCurrentPayloadAsync(
                    recordPath,
                    record,
                    cancellationToken);
                summaries.Add(record.Summary);
            }

            return summaries
                .OrderBy(profile => profile.IsDeleted)
                .ThenBy(
                    profile => profile.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            accountLock.Release();
        }
    }

    public async Task<int> MigrateAllAccountsToCurrentStateAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_profilesRoot))
        {
            return 0;
        }

        var migratedAccounts = 0;
        foreach (var accountDirectory in Directory.EnumerateDirectories(
                     _profilesRoot,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recordPaths = Directory.EnumerateFiles(
                    accountDirectory,
                    CurrentRecordFileName,
                    SearchOption.AllDirectories)
                .Take(ProfileSyncContract.MaximumRemoteProfiles + 1)
                .ToList();
            if (recordPaths.Count == 0)
            {
                continue;
            }

            if (recordPaths.Count > ProfileSyncContract.MaximumRemoteProfiles)
            {
                throw new InvalidDataException(
                    "An account contains more profile records than protocol v1 permits.");
            }

            var records = new List<(string Path, StoredProfileRecord Record)>(
                recordPaths.Count);
            foreach (var recordPath in recordPaths)
            {
                records.Add((
                    recordPath,
                    await ReadRecordAsync(recordPath, cancellationToken)));
            }

            // Preserve the newest live state. A tombstone is retained only
            // when the account has no live settings payload at all.
            var retained = records
                .OrderBy(item => item.Record.Summary.IsDeleted)
                .ThenByDescending(item => item.Record.Summary.UpdatedAtUtc)
                .ThenByDescending(item => item.Record.Summary.ServerRevision)
                .First();
            await MigrateToCurrentPayloadAsync(
                retained.Path,
                retained.Record,
                cancellationToken);
            var retainedProfileDirectory = Path.GetDirectoryName(retained.Path)
                                           ?? throw new InvalidDataException(
                                               "A stored profile record has no parent directory.");
            PruneOtherProfileDirectories(
                accountDirectory,
                retainedProfileDirectory);
            migratedAccounts++;
        }

        return migratedAccounts;
    }

    public async Task<StoredProfileFetch?> FetchAsync(
        string accountId,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var normalizedAccount = ProfileServerIdentity.NormalizeAccountId(accountId);
        var normalizedProfile = ProfileServerIdentity.ValidateProfileId(profileId);
        var accountLock = GetAccountLock(normalizedAccount);
        await accountLock.WaitAsync(cancellationToken);
        try
        {
            var profileDirectory = GetProfileDirectory(
                normalizedAccount,
                normalizedProfile);
            var recordPath = Path.Combine(
                profileDirectory,
                CurrentRecordFileName);
            if (!File.Exists(recordPath))
            {
                return null;
            }

            var record = await ReadRecordAsync(
                recordPath,
                cancellationToken);
            if (record.Summary.IsDeleted)
            {
                return new StoredProfileFetch(record.Summary, ReadOnlyMemory<byte>.Empty);
            }

            var contentPath = ResolveContentPath(
                profileDirectory,
                record.ContentFile);
            var info = new FileInfo(contentPath);
            if (!info.Exists
                || info.Length is <= 0
                || info.Length > ProfileContract.MaximumBundleBytes)
            {
                throw new InvalidDataException(
                    $"Stored content for profile '{normalizedProfile}' is missing or invalid.");
            }

            var payload = await File.ReadAllBytesAsync(
                contentPath,
                cancellationToken);
            var validated = _validator.Validate(payload, normalizedProfile);
            if (!string.Equals(
                    validated.ProfileSha256,
                    record.Summary.ProfileSha256,
                    StringComparison.OrdinalIgnoreCase)
                || validated.ProfileRevision != record.Summary.ProfileRevision)
            {
                throw new InvalidDataException(
                    $"Stored metadata for profile '{normalizedProfile}' does not match its payload.");
            }

            return new StoredProfileFetch(record.Summary, payload);
        }
        finally
        {
            accountLock.Release();
        }
    }

    public async Task<ProfileStoreMutation> PutAsync(
        string accountId,
        ValidatedServerProfile profile,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var normalizedAccount = ProfileServerIdentity.NormalizeAccountId(accountId);
        var normalizedProfile = ProfileServerIdentity.ValidateProfileId(profile.Id);
        var accountLock = GetAccountLock(normalizedAccount);
        await accountLock.WaitAsync(cancellationToken);
        try
        {
            var accountDirectory = GetAccountDirectory(normalizedAccount);
            var profileDirectory = GetProfileDirectory(
                normalizedAccount,
                normalizedProfile);
            var recordPath = Path.Combine(
                profileDirectory,
                CurrentRecordFileName);
            var current = File.Exists(recordPath)
                ? await ReadRecordAsync(recordPath, cancellationToken)
                : null;
            if (expectedETag is null)
            {
                if (current is not null)
                {
                    return new ProfileStoreMutation(
                        ProfileStoreMutationKind.Conflict);
                }
            }
            else if (current is null
                     || !string.Equals(
                         current.Summary.ETag,
                         expectedETag,
                         StringComparison.Ordinal))
            {
                return new ProfileStoreMutation(
                    ProfileStoreMutationKind.Conflict);
            }

            // The account model retains one current settings payload. A write
            // replaces that payload and removes superseded profile directories,
            // so historical bytes must not count against the resulting state.
            if (profile.Payload.Length > _maximumAccountStorageBytes)
            {
                return new ProfileStoreMutation(
                    ProfileStoreMutationKind.StorageLimit);
            }

            var serverRevision = checked(
                (current?.Summary.ServerRevision ?? 0) + 1);
            var eTag = CreateETag(serverRevision, profile.ProfileSha256);
            var summary = new RemoteProfileSummary
            {
                Id = profile.Id,
                DisplayName = profile.DisplayName,
                ServerRevision = serverRevision,
                ProfileRevision = profile.ProfileRevision,
                UpdatedAtUtc = profile.UpdatedAtUtc,
                ProfileSha256 = profile.ProfileSha256,
                ETag = eTag,
                IsDeleted = false
            };
            var contentFile = GetCurrentPayloadFileName(serverRevision);
            var contentPath = ResolveContentPath(
                profileDirectory,
                contentFile);
            await AtomicFile.WriteAsync(
                contentPath,
                profile.Payload,
                restrictToOwner: true,
                cancellationToken);
            await WriteRecordAsync(
                recordPath,
                new StoredProfileRecord
                {
                    Summary = summary,
                    ContentFile = contentFile
                },
                cancellationToken);
            PruneSupersededPayloads(profileDirectory, contentFile);
            PruneOtherProfileDirectories(
                accountDirectory,
                profileDirectory);
            return new ProfileStoreMutation(
                current is null
                    ? ProfileStoreMutationKind.Created
                    : ProfileStoreMutationKind.Updated,
                summary);
        }
        finally
        {
            accountLock.Release();
        }
    }

    public async Task<ProfileStoreMutation> DeleteAsync(
        string accountId,
        string profileId,
        string expectedETag,
        CancellationToken cancellationToken = default)
    {
        var normalizedAccount = ProfileServerIdentity.NormalizeAccountId(accountId);
        var normalizedProfile = ProfileServerIdentity.ValidateProfileId(profileId);
        var accountLock = GetAccountLock(normalizedAccount);
        await accountLock.WaitAsync(cancellationToken);
        try
        {
            var profileDirectory = GetProfileDirectory(
                normalizedAccount,
                normalizedProfile);
            var recordPath = Path.Combine(
                profileDirectory,
                CurrentRecordFileName);
            if (!File.Exists(recordPath))
            {
                return new ProfileStoreMutation(
                    ProfileStoreMutationKind.Conflict);
            }

            var current = await ReadRecordAsync(
                recordPath,
                cancellationToken);
            if (current.Summary.IsDeleted
                || !string.Equals(
                    current.Summary.ETag,
                    expectedETag,
                    StringComparison.Ordinal))
            {
                return new ProfileStoreMutation(
                    ProfileStoreMutationKind.Conflict);
            }

            var serverRevision = checked(
                current.Summary.ServerRevision + 1);
            var summary = new RemoteProfileSummary
            {
                Id = current.Summary.Id,
                DisplayName = current.Summary.DisplayName,
                ServerRevision = serverRevision,
                ProfileRevision = current.Summary.ProfileRevision,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                ProfileSha256 = current.Summary.ProfileSha256,
                ETag = CreateETag(
                    serverRevision,
                    current.Summary.ProfileSha256),
                IsDeleted = true
            };
            await WriteRecordAsync(
                recordPath,
                new StoredProfileRecord
                {
                    Summary = summary,
                    ContentFile = current.ContentFile
                },
                cancellationToken);
            DeleteProfilePayloads(profileDirectory);
            return new ProfileStoreMutation(
                ProfileStoreMutationKind.Deleted,
                summary);
        }
        finally
        {
            accountLock.Release();
        }
    }

    private SemaphoreSlim GetAccountLock(string accountId) =>
        _accountLocks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));

    private string GetAccountDirectory(string accountId) =>
        Path.Combine(
            _profilesRoot,
            ProfileServerIdentity.StorageKey(accountId));

    private string GetProfileDirectory(
        string accountId,
        string profileId) =>
        Path.Combine(
            GetAccountDirectory(accountId),
            ProfileServerIdentity.StorageKey(profileId));

    private static string ResolveContentPath(
        string profileDirectory,
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathFullyQualified(relativePath))
        {
            throw new InvalidDataException(
                "A stored profile content path is malformed.");
        }

        var root = Path.GetFullPath(profileDirectory)
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(
            Path.Combine(
                root,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "A stored profile content path escaped its profile directory.");
        }

        return fullPath;
    }

    private static void PruneSupersededPayloads(
        string profileDirectory,
        string retainedContentFile)
    {
        if (!Directory.Exists(profileDirectory))
        {
            return;
        }

        var retainedPath = ResolveContentPath(
            profileDirectory,
            retainedContentFile);
        foreach (var path in Directory.EnumerateFiles(
                     profileDirectory,
                     $"*{ProfileContract.FileExtension}",
                     SearchOption.AllDirectories))
        {
            if (!string.Equals(
                    Path.GetFullPath(path),
                    retainedPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(path);
            }
        }

        var legacyRevisionDirectory = Path.Combine(
            profileDirectory,
            "revisions");
        TryDeleteEmptyDirectory(legacyRevisionDirectory);
    }

    private static async Task<StoredProfileRecord> MigrateToCurrentPayloadAsync(
        string recordPath,
        StoredProfileRecord record,
        CancellationToken cancellationToken)
    {
        var profileDirectory = Path.GetDirectoryName(recordPath)
                               ?? throw new InvalidDataException(
                                   "A stored profile record has no parent directory.");
        var migrated = record;
        var currentContentFile = GetCurrentPayloadFileName(
            record.Summary.ServerRevision);
        if (!string.Equals(
                record.ContentFile,
                currentContentFile,
                StringComparison.OrdinalIgnoreCase))
        {
            if (!record.Summary.IsDeleted)
            {
                var legacyPath = ResolveContentPath(
                    profileDirectory,
                    record.ContentFile);
                var payload = await File.ReadAllBytesAsync(
                    legacyPath,
                    cancellationToken);
                await AtomicFile.WriteAsync(
                    ResolveContentPath(
                        profileDirectory,
                        currentContentFile),
                    payload,
                    restrictToOwner: true,
                    cancellationToken);
            }

            migrated = new StoredProfileRecord
            {
                Summary = record.Summary,
                ContentFile = currentContentFile
            };
            await WriteRecordAsync(
                recordPath,
                migrated,
                cancellationToken);
        }

        if (migrated.Summary.IsDeleted)
        {
            DeleteProfilePayloads(profileDirectory);
        }
        else
        {
            PruneSupersededPayloads(
                profileDirectory,
                migrated.ContentFile);
        }

        return migrated;
    }

    private static void DeleteProfilePayloads(string profileDirectory)
    {
        if (!Directory.Exists(profileDirectory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(
                     profileDirectory,
                     $"*{ProfileContract.FileExtension}",
                     SearchOption.AllDirectories))
        {
            TryDeleteFile(path);
        }

        TryDeleteEmptyDirectory(Path.Combine(profileDirectory, "revisions"));
    }

    private static void PruneOtherProfileDirectories(
        string accountDirectory,
        string retainedProfileDirectory)
    {
        if (!Directory.Exists(accountDirectory))
        {
            return;
        }

        var accountRoot = Path.GetFullPath(accountDirectory)
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var retained = Path.GetFullPath(retainedProfileDirectory)
            .TrimEnd(Path.DirectorySeparatorChar);
        foreach (var directory in Directory.EnumerateDirectories(
                     accountDirectory,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var candidate = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar);
            if (candidate.StartsWith(accountRoot, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(
                    candidate,
                    retained,
                    StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteDirectory(candidate);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException)
        {
            // A subsequent list or write retries cleanup. The current record
            // already points only at the retained payload.
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)
                && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException)
        {
            // Cleanup is retried after the next successful account write.
        }
    }

    private static string GetCurrentPayloadFileName(long serverRevision) =>
        $"{CurrentPayloadPrefix}{serverRevision:D20}{ProfileContract.FileExtension}";

    private static string CreateETag(long revision, string profileSha256) =>
        $"\"p-{revision}-{profileSha256[..16].ToLowerInvariant()}\"";

    private static async Task<StoredProfileRecord> ReadRecordAsync(
        string recordPath,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(recordPath);
        if (!info.Exists || info.Length is <= 0 or > 64 * 1024)
        {
            throw new InvalidDataException(
                "A stored profile record is missing or oversized.");
        }

        var payload = await File.ReadAllBytesAsync(
            recordPath,
            cancellationToken);
        var record = JsonSerializer.Deserialize<StoredProfileRecord>(
                         payload,
                         JsonOptions)
                     ?? throw new InvalidDataException(
                         "A stored profile record was empty.");
        ValidateRecord(record);
        return record;
    }

    private static async Task WriteRecordAsync(
        string recordPath,
        StoredProfileRecord record,
        CancellationToken cancellationToken)
    {
        ValidateRecord(record);
        await AtomicFile.WriteAsync(
            recordPath,
            JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions),
            restrictToOwner: true,
            cancellationToken);
    }

    private static void ValidateRecord(StoredProfileRecord record)
    {
        var summary = record.Summary;
        _ = ProfileServerIdentity.ValidateProfileId(summary.Id);
        if (record.SchemaVersion != StoredProfileRecord.CurrentSchemaVersion
            || string.IsNullOrWhiteSpace(summary.DisplayName)
            || summary.DisplayName.Length > 120
            || summary.ServerRevision < 1
            || summary.ProfileRevision < 0
            || summary.UpdatedAtUtc == default
            || summary.ProfileSha256.Length != 64
            || !summary.ProfileSha256.All(Uri.IsHexDigit)
            || !IsStrongETag(summary.ETag))
        {
            throw new InvalidDataException(
                $"Stored metadata for profile '{summary.Id}' is malformed.");
        }

        var normalizedContentFile = record.ContentFile.Replace('\\', '/');
        var contentSegments = normalizedContentFile
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var isCurrentPayload = contentSegments.Length == 1
            && contentSegments[0].StartsWith(
                CurrentPayloadPrefix,
                StringComparison.OrdinalIgnoreCase)
            && contentSegments[0].EndsWith(
                ProfileContract.FileExtension,
                StringComparison.OrdinalIgnoreCase)
            && Path.GetFileNameWithoutExtension(contentSegments[0])
                [CurrentPayloadPrefix.Length..]
                is { Length: 20 } currentRevision
            && currentRevision.All(char.IsDigit);
        var isLegacyRevision = contentSegments.Length == 2
            && contentSegments[0] == "revisions"
            && contentSegments[1].EndsWith(
                ProfileContract.FileExtension,
                StringComparison.OrdinalIgnoreCase)
            && Path.GetFileNameWithoutExtension(contentSegments[1]).Length == 20
            && Path.GetFileNameWithoutExtension(contentSegments[1]).All(char.IsDigit);
        if (!isCurrentPayload && !isLegacyRevision)
        {
            throw new InvalidDataException(
                "A stored profile content filename is malformed.");
        }
    }

    private static bool IsStrongETag(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.StartsWith("W/", StringComparison.OrdinalIgnoreCase)
        && value.Length is >= 3 and <= 258
        && value[0] == '"'
        && value[^1] == '"'
        && !value.Any(char.IsControl);

    private sealed class StoredProfileRecord
    {
        public const int CurrentSchemaVersion = 1;

        [JsonRequired]
        public int SchemaVersion { get; init; } = CurrentSchemaVersion;

        [JsonRequired]
        public RemoteProfileSummary Summary { get; init; } = new();

        [JsonRequired]
        public string ContentFile { get; init; } = string.Empty;
    }
}
