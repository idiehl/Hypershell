using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Hypershell.ProfileServer;

public sealed record ProfileSessionPrincipal(
    string AccountId,
    string AccountDisplayName,
    int CredentialVersion,
    DateTimeOffset ExpiresAtUtc,
    string? ClientDeviceId = null);

public sealed record IssuedProfileSession(
    string AccessToken,
    ProfileSessionPrincipal Principal);

public sealed record ProfileSessionSummary(
    string Id,
    string AccountId,
    string AccountDisplayName,
    string ClientDeviceId,
    DateTimeOffset ExpiresAtUtc);

public sealed class ProfileSessionTokenService
{
    private readonly ConcurrentDictionary<string, ProfileSessionPrincipal>
        _sessions = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _sessionLifetime;

    public ProfileSessionTokenService(
        ProfileServerOptions options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalized = options.Normalize();
        _sessionLifetime = normalized.SessionLifetime;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IssuedProfileSession Issue(
        ProfileAccountState account,
        string? clientDeviceId = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.IsDisabled)
        {
            throw new InvalidOperationException(
                "Disabled accounts cannot receive sessions.");
        }

        RemoveExpired();
        var rawToken = "hps_" + Base64Url(
            RandomNumberGenerator.GetBytes(32));
        var principal = new ProfileSessionPrincipal(
            account.AccountId,
            account.DisplayName,
            account.CredentialVersion,
            _timeProvider.GetUtcNow().Add(_sessionLifetime),
            clientDeviceId);
        if (!_sessions.TryAdd(HashToken(rawToken), principal))
        {
            throw new CryptographicException(
                "A unique profile session token could not be issued.");
        }

        return new IssuedProfileSession(rawToken, principal);
    }

    public bool TryAuthenticate(
        string accessToken,
        out ProfileSessionPrincipal? principal)
    {
        principal = null;
        if (string.IsNullOrWhiteSpace(accessToken)
            || accessToken.Length > 8192
            || accessToken.Any(character => character is '\r' or '\n'))
        {
            return false;
        }

        var key = HashToken(accessToken);
        if (!_sessions.TryGetValue(key, out var candidate))
        {
            return false;
        }

        if (candidate.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            _sessions.TryRemove(key, out _);
            return false;
        }

        principal = candidate;
        return true;
    }

    public void Revoke(string accessToken)
    {
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            _sessions.TryRemove(HashToken(accessToken), out _);
        }
    }

    public void RevokeClientDevice(string accountId, string clientDeviceId)
    {
        foreach (var session in _sessions)
        {
            if (string.Equals(
                    session.Value.AccountId,
                    accountId,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    session.Value.ClientDeviceId,
                    clientDeviceId,
                    StringComparison.Ordinal))
            {
                _sessions.TryRemove(session.Key, out _);
            }
        }
    }

    public IReadOnlyList<ProfileSessionSummary> ListActive(string accountId)
    {
        var normalized = ProfileServerIdentity.NormalizeAccountId(accountId);
        RemoveExpired();
        return _sessions
            .Where(session => string.Equals(
                session.Value.AccountId,
                normalized,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(session => session.Value.ExpiresAtUtc)
            .Select(session => new ProfileSessionSummary(
                session.Key,
                session.Value.AccountId,
                session.Value.AccountDisplayName,
                session.Value.ClientDeviceId ?? string.Empty,
                session.Value.ExpiresAtUtc))
            .ToList();
    }

    public bool RevokeById(string accountId, string sessionId)
    {
        var normalized = ProfileServerIdentity.NormalizeAccountId(accountId);
        if (string.IsNullOrWhiteSpace(sessionId)
            || sessionId.Length != 64
            || !sessionId.All(char.IsAsciiHexDigit)
            || !_sessions.TryGetValue(sessionId.ToUpperInvariant(), out var principal)
            || !string.Equals(
                principal.AccountId,
                normalized,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _sessions.TryRemove(sessionId.ToUpperInvariant(), out _);
    }

    private void RemoveExpired()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var session in _sessions)
        {
            if (session.Value.ExpiresAtUtc <= now)
            {
                _sessions.TryRemove(session.Key, out _);
            }
        }
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
