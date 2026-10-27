using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hypershell.ProfileServer;

public sealed record AccountSecurityEnrollment(
    string AccountId,
    string TotpSecret,
    string ProvisioningUri,
    IReadOnlyList<string> RecoveryCodes);

public sealed record AccountPasswordVerification(
    bool Succeeded,
    bool UsedRecoveryCode,
    DateTimeOffset? LockedUntilUtc);

public sealed class AccountSecurityStore
{
    private const int TotpSecretBytes = 20;
    private const int TotpDigits = 6;
    private const int TotpPeriodSeconds = 30;
    private const int MaximumFailures = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly CloudMetadataStore _metadata;
    private readonly ProfileAccountStore _accounts;
    private readonly IDataProtector _protector;
    private readonly PasswordHasher<AccountSecurityUser> _passwordHasher = new();
    private readonly TimeProvider _timeProvider;

    public AccountSecurityStore(
        CloudMetadataStore metadata,
        ProfileAccountStore accounts,
        IDataProtectionProvider protectionProvider,
        TimeProvider timeProvider)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        ArgumentNullException.ThrowIfNull(protectionProvider);
        _protector = protectionProvider.CreateProtector(
            "Hypershell.AccountSecurity.v1");
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<AccountSecurityEnrollment> SetPasswordAsync(
        string accountId,
        string password,
        CancellationToken cancellationToken = default)
    {
        var normalizedId = ProfileServerIdentity.NormalizeAccountId(accountId);
        _ = await _accounts.FindAsync(normalizedId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Account '{normalizedId}' does not exist.");
        ValidatePassword(password);

        var user = new AccountSecurityUser(normalizedId);
        var passwordHash = _passwordHasher.HashPassword(user, password);
        var totpSecret = RandomNumberGenerator.GetBytes(TotpSecretBytes);
        var recoveryCodes = Enumerable.Range(0, 10)
            .Select(_ => CreateRecoveryCode())
            .ToArray();
        var recoveryHashes = recoveryCodes
            .Select(HashRecoveryCode)
            .ToArray();
        var encryptedSecret = _protector.Protect(totpSecret);
        var encryptedCodes = _protector.Protect(
            JsonSerializer.SerializeToUtf8Bytes(recoveryHashes));

        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO account_security(
                account_id,
                password_hash,
                encrypted_totp_secret,
                encrypted_recovery_codes,
                failed_sign_in_count,
                locked_until_utc,
                updated_at_utc)
            VALUES(
                $accountId,
                $passwordHash,
                $totpSecret,
                $recoveryCodes,
                0,
                NULL,
                $updatedAtUtc)
            ON CONFLICT(account_id) DO UPDATE SET
                password_hash = excluded.password_hash,
                encrypted_totp_secret = excluded.encrypted_totp_secret,
                encrypted_recovery_codes = excluded.encrypted_recovery_codes,
                failed_sign_in_count = 0,
                locked_until_utc = NULL,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$accountId", normalizedId);
        command.Parameters.AddWithValue("$passwordHash", passwordHash);
        command.Parameters.Add("$totpSecret", SqliteType.Blob).Value = encryptedSecret;
        command.Parameters.Add("$recoveryCodes", SqliteType.Blob).Value = encryptedCodes;
        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            _timeProvider.GetUtcNow().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        CryptographicOperations.ZeroMemory(totpSecret);
        var encodedSecret = Base32.Encode(
            _protector.Unprotect(encryptedSecret));
        var label = Uri.EscapeDataString($"Hypershell:{normalizedId}");
        var provisioningUri =
            $"otpauth://totp/{label}?secret={encodedSecret}&issuer=Hypershell&digits={TotpDigits}&period={TotpPeriodSeconds}";
        return new AccountSecurityEnrollment(
            normalizedId,
            encodedSecret,
            provisioningUri,
            recoveryCodes);
    }

    public async Task<bool> IsEnrolledAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        var normalizedId = ProfileServerIdentity.NormalizeAccountId(accountId);
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM account_security WHERE account_id = $accountId AND password_hash IS NOT NULL;";
        command.Parameters.AddWithValue("$accountId", normalizedId);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<AccountPasswordVerification> VerifyAsync(
        string accountId,
        string password,
        string secondFactor,
        CancellationToken cancellationToken = default)
    {
        var normalizedId = TryNormalize(accountId);
        var now = _timeProvider.GetUtcNow();
        await using var connection = _metadata.OpenConnection();
        await connection.OpenAsync(cancellationToken);
        var record = normalizedId is null
            ? null
            : await ReadAsync(connection, normalizedId, cancellationToken);

        if (record?.LockedUntilUtc is { } lockedUntil && lockedUntil > now)
        {
            PerformDummyPasswordCheck(password);
            return new AccountPasswordVerification(false, false, lockedUntil);
        }

        if (record is null || normalizedId is null)
        {
            PerformDummyPasswordCheck(password);
            return new AccountPasswordVerification(false, false, null);
        }

        var user = new AccountSecurityUser(normalizedId);
        var passwordResult = _passwordHasher.VerifyHashedPassword(
            user,
            record.PasswordHash,
            password ?? string.Empty);
        var usedRecoveryCode = false;
        var secondFactorValid = false;
        string[]? remainingRecoveryHashes = null;
        if (passwordResult != PasswordVerificationResult.Failed)
        {
            var secret = _protector.Unprotect(record.EncryptedTotpSecret);
            try
            {
                secondFactorValid = VerifyTotp(secret, secondFactor, now);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
            }

            if (!secondFactorValid)
            {
                var hashes = JsonSerializer.Deserialize<string[]>(
                                 _protector.Unprotect(
                                     record.EncryptedRecoveryCodes))
                             ?? [];
                var suppliedHash = HashRecoveryCode(secondFactor);
                var match = Array.FindIndex(
                    hashes,
                    hash => FixedTimeEquals(hash, suppliedHash));
                if (match >= 0)
                {
                    secondFactorValid = true;
                    usedRecoveryCode = true;
                    remainingRecoveryHashes = hashes
                        .Where((_, index) => index != match)
                        .ToArray();
                }
            }
        }

        if (passwordResult == PasswordVerificationResult.Failed
            || !secondFactorValid)
        {
            var failures = checked(record.FailedSignInCount + 1);
            var lockUntil = failures >= MaximumFailures
                ? now.Add(LockoutDuration)
                : (DateTimeOffset?)null;
            await UpdateFailureAsync(
                connection,
                normalizedId,
                lockUntil is null ? failures : 0,
                lockUntil,
                cancellationToken);
            return new AccountPasswordVerification(false, false, lockUntil);
        }

        await using var success = connection.CreateCommand();
        success.CommandText =
            """
            UPDATE account_security
            SET password_hash = $passwordHash,
                encrypted_recovery_codes = COALESCE($recoveryCodes, encrypted_recovery_codes),
                failed_sign_in_count = 0,
                locked_until_utc = NULL,
                updated_at_utc = $updatedAtUtc
            WHERE account_id = $accountId;
            """;
        success.Parameters.AddWithValue("$accountId", normalizedId);
        success.Parameters.AddWithValue(
            "$passwordHash",
            passwordResult == PasswordVerificationResult.SuccessRehashNeeded
                ? _passwordHasher.HashPassword(user, password!)
                : record.PasswordHash);
        success.Parameters.Add("$recoveryCodes", SqliteType.Blob).Value =
            remainingRecoveryHashes is null
                ? DBNull.Value
                : _protector.Protect(
                    JsonSerializer.SerializeToUtf8Bytes(
                        remainingRecoveryHashes));
        success.Parameters.AddWithValue("$updatedAtUtc", now.ToString("O"));
        await success.ExecuteNonQueryAsync(cancellationToken);
        return new AccountPasswordVerification(true, usedRecoveryCode, null);
    }

    public static string GenerateTotpForTesting(
        string base32Secret,
        DateTimeOffset timestamp) =>
        GenerateTotp(Base32.Decode(base32Secret), timestamp);

    private static async Task<AccountSecurityRecord?> ReadAsync(
        SqliteConnection connection,
        string accountId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT password_hash,
                   encrypted_totp_secret,
                   encrypted_recovery_codes,
                   failed_sign_in_count,
                   locked_until_utc
            FROM account_security
            WHERE account_id = $accountId;
            """;
        command.Parameters.AddWithValue("$accountId", accountId);
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var lockedText = reader.IsDBNull(4) ? null : reader.GetString(4);
        return new AccountSecurityRecord(
            reader.GetString(0),
            (byte[])reader[1],
            (byte[])reader[2],
            reader.GetInt32(3),
            lockedText is null
                ? null
                : DateTimeOffset.Parse(
                    lockedText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind));
    }

    private async Task UpdateFailureAsync(
        SqliteConnection connection,
        string accountId,
        int failures,
        DateTimeOffset? lockedUntil,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE account_security
            SET failed_sign_in_count = $failures,
                locked_until_utc = $lockedUntilUtc,
                updated_at_utc = $updatedAtUtc
            WHERE account_id = $accountId;
            """;
        command.Parameters.AddWithValue("$accountId", accountId);
        command.Parameters.AddWithValue("$failures", failures);
        command.Parameters.AddWithValue(
            "$lockedUntilUtc",
            lockedUntil is null ? DBNull.Value : lockedUntil.Value.ToString("O"));
        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            _timeProvider.GetUtcNow().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void PerformDummyPasswordCheck(string password)
    {
        const string dummyHash =
            "AQAAAAIAAYagAAAAEBpRjCmTmT7h4caYfMpDOClRf8K/tyX1XjNsj1f2kHu0Y1rkC1XRWgMbCH4+QmWTrA==";
        _ = _passwordHasher.VerifyHashedPassword(
            new AccountSecurityUser("dummy"),
            dummyHash,
            password ?? string.Empty);
    }

    private static bool VerifyTotp(
        byte[] secret,
        string supplied,
        DateTimeOffset timestamp)
    {
        var normalized = (supplied ?? string.Empty)
            .Where(char.IsAsciiDigit)
            .ToArray();
        if (normalized.Length != TotpDigits)
        {
            return false;
        }

        var candidate = new string(normalized);
        for (var offset = -1; offset <= 1; offset++)
        {
            var expected = GenerateTotp(
                secret,
                timestamp.AddSeconds(offset * TotpPeriodSeconds));
            if (FixedTimeEquals(candidate, expected))
            {
                return true;
            }
        }

        return false;
    }

    private static string GenerateTotp(
        byte[] secret,
        DateTimeOffset timestamp)
    {
        var counter = timestamp.ToUnixTimeSeconds() / TotpPeriodSeconds;
        Span<byte> counterBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(
            counterBytes,
            counter);
        using var hmac = new HMACSHA1(secret);
        var hash = hmac.ComputeHash(counterBytes.ToArray());
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24)
                     | ((hash[offset + 1] & 0xff) << 16)
                     | ((hash[offset + 2] & 0xff) << 8)
                     | (hash[offset + 3] & 0xff);
        var value = binary % 1_000_000;
        return value.ToString("D6", CultureInfo.InvariantCulture);
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password)
            || password.Length is < 8 or > 256
            || password.Any(char.IsControl))
        {
            throw new ArgumentException(
                "The account password must contain 8-256 printable characters.",
                nameof(password));
        }
    }

    private static string CreateRecoveryCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(12);
        var characters = bytes
            .Select(value => alphabet[value % alphabet.Length])
            .ToArray();
        return $"{new string(characters, 0, 4)}-{new string(characters, 4, 4)}-{new string(characters, 8, 4)}";
    }

    private static string HashRecoveryCode(string code) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        (code ?? string.Empty)
                        .Replace("-", string.Empty, StringComparison.Ordinal)
                        .Trim()
                        .ToUpperInvariant())))
            .ToLowerInvariant();

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(
                   leftBytes,
                   rightBytes);
    }

    private static string? TryNormalize(string value)
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

    private sealed record AccountSecurityUser(string AccountId);

    private sealed record AccountSecurityRecord(
        string PasswordHash,
        byte[] EncryptedTotpSecret,
        byte[] EncryptedRecoveryCodes,
        int FailedSignInCount,
        DateTimeOffset? LockedUntilUtc);

    private static class Base32
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public static string Encode(ReadOnlySpan<byte> data)
        {
            var output = new StringBuilder((data.Length * 8 + 4) / 5);
            var buffer = 0;
            var bits = 0;
            foreach (var value in data)
            {
                buffer = (buffer << 8) | value;
                bits += 8;
                while (bits >= 5)
                {
                    bits -= 5;
                    output.Append(Alphabet[(buffer >> bits) & 31]);
                }
            }

            if (bits > 0)
            {
                output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
            }

            return output.ToString();
        }

        public static byte[] Decode(string value)
        {
            var normalized = (value ?? string.Empty)
                .Trim()
                .Replace("=", string.Empty, StringComparison.Ordinal)
                .ToUpperInvariant();
            var output = new List<byte>((normalized.Length * 5) / 8);
            var buffer = 0;
            var bits = 0;
            foreach (var character in normalized)
            {
                var index = Alphabet.IndexOf(character);
                if (index < 0)
                {
                    throw new FormatException("The TOTP secret is not valid base32.");
                }

                buffer = (buffer << 5) | index;
                bits += 5;
                if (bits >= 8)
                {
                    bits -= 8;
                    output.Add((byte)((buffer >> bits) & 0xff));
                }
            }

            return output.ToArray();
        }
    }
}
