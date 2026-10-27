using System.Text.Json.Serialization;

namespace Hypershell.Profiles;

public static class ProfileSessionContract
{
    public const int CurrentProtocolVersion = 1;
    public const string RelativeSessionPath =
        "api/hypershell/v1/auth/session";
    public const long MaximumSignInRequestBytes = 16 * 1024;
    public const long MaximumSessionResponseBytes = 64 * 1024;
}

public sealed class ProfileSessionRequest
{
    [JsonRequired]
    public int ProtocolVersion { get; init; } =
        ProfileSessionContract.CurrentProtocolVersion;

    [JsonRequired]
    public string AccountId { get; init; } = string.Empty;

    [JsonRequired]
    public string AccessKey { get; init; } = string.Empty;
}

public sealed class ProfileSessionResponse
{
    [JsonRequired]
    public int ProtocolVersion { get; init; } =
        ProfileSessionContract.CurrentProtocolVersion;

    [JsonRequired]
    public string AccountId { get; init; } = string.Empty;

    [JsonRequired]
    public string AccountDisplayName { get; init; } = string.Empty;

    [JsonRequired]
    public string AccessToken { get; init; } = string.Empty;

    [JsonRequired]
    public DateTimeOffset ExpiresAtUtc { get; init; }
}
