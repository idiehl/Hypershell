using Hypershell.Profiles;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

public enum SshCredentialReleaseClientKind
{
    Pending,
    Approved,
    Expired,
    Denied,
    Consumed,
    Changed,
    Invalid
}

public sealed record SshCredentialReleaseClientResult(
    SshCredentialReleaseClientKind Kind,
    SshCredentialReleaseResponse? Credential = null);

public sealed record LinkedHostInventorySnapshot(
    IReadOnlyList<RemoteHostSummary> Hosts,
    IReadOnlyList<SshCredentialSummary> Credentials,
    IReadOnlySet<string> LocallyUnlockedCredentialIds);

public sealed record SshCredentialUnlockOutcome(
    string CredentialId,
    int KeyVersion,
    string PublicKeyFingerprint);

/// <summary>
/// Strict authenticated client for linked-host metadata and the SSH vault.
/// Secret-bearing JSON buffers are cleared immediately after transmission or
/// deserialization.
/// </summary>
public sealed class HttpLinkedHostClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false)
        }
    };

    private readonly HttpClient _client;
    private readonly Uri _endpoint;
    private readonly TimeSpan _timeout;
    private bool _disposed;

    public HttpLinkedHostClient(
        string serverEndpoint,
        string accessToken,
        HttpMessageHandler? handler = null,
        TimeSpan? requestTimeout = null)
    {
        _endpoint = ProfileSyncEndpoint.Normalize(serverEndpoint);
        ValidateAccessToken(accessToken);
        _timeout = requestTimeout ?? TimeSpan.FromSeconds(60);
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromMinutes(10))
        {
            throw Failure(
                ProfileSyncFailureKind.Configuration,
                "Linked-host requests must time out within ten minutes.");
        }

        handler ??= new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression =
                DecompressionMethods.GZip
                | DecompressionMethods.Deflate
                | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        _client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"Hypershell/{GetApplicationVersion()}");
        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<RemoteHostSummary>> ListHostsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                CreateUri(HypershellCloudContract.RelativeRemoteHostsPath)),
            cancellationToken);
        EnsureSuccess(response, "list linked hosts");
        var document = await ReadJsonAsync<RemoteHostList>(
            response,
            HypershellCloudContract.MaximumRemoteHostListBytes,
            cancellationToken);
        ValidateProtocol(document.ProtocolVersion);
        return document.Hosts;
    }

    public async Task<RemoteHostSummary> CreateHostAsync(
        RemoteHostMutationRequest host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        using var response = await SendJsonAsync(
            HttpMethod.Post,
            HypershellCloudContract.RelativeRemoteHostsPath,
            host,
            cancellationToken);
        EnsureSuccess(response, "create linked host");
        return (await ReadJsonAsync<RemoteHostMutationReceipt>(
            response,
            HypershellCloudContract.MaximumRemoteHostMutationBytes,
            cancellationToken)).Host;
    }

    public async Task<RemoteHostSummary> UpdateHostAsync(
        string hostId,
        RemoteHostMutationRequest host,
        CancellationToken cancellationToken = default)
    {
        ValidateId(hostId, "remote-host");
        ArgumentNullException.ThrowIfNull(host);
        using var response = await SendJsonAsync(
            HttpMethod.Put,
            HypershellCloudContract.RelativeRemoteHostsPath
            + "/"
            + Uri.EscapeDataString(hostId),
            host,
            cancellationToken);
        EnsureSuccess(response, "update linked host");
        return (await ReadJsonAsync<RemoteHostMutationReceipt>(
            response,
            HypershellCloudContract.MaximumRemoteHostMutationBytes,
            cancellationToken)).Host;
    }

    public async Task<RemoteHostSummary> PinHostKeyAsync(
        string hostId,
        RemoteHostKeyPinRequest pin,
        CancellationToken cancellationToken = default)
    {
        ValidateId(hostId, "remote-host");
        ArgumentNullException.ThrowIfNull(pin);
        using var response = await SendJsonAsync(
            HttpMethod.Put,
            HypershellCloudContract.RelativeRemoteHostsPath
            + "/"
            + Uri.EscapeDataString(hostId)
            + "/host-key",
            pin,
            cancellationToken);
        EnsureSuccess(response, "pin the SSH host key");
        return (await ReadJsonAsync<RemoteHostMutationReceipt>(
            response,
            HypershellCloudContract.MaximumRemoteHostMutationBytes,
            cancellationToken)).Host;
    }

    public async Task DisableHostAsync(
        string hostId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(hostId, "remote-host");
        using var response = await SendAsync(
            new HttpRequestMessage(
                HttpMethod.Delete,
                CreateUri(
                    HypershellCloudContract.RelativeRemoteHostsPath
                    + "/"
                    + Uri.EscapeDataString(hostId))),
            cancellationToken);
        EnsureSuccess(response, "disable linked host");
    }

    public async Task<IReadOnlyList<SshCredentialSummary>>
        ListCredentialsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                CreateUri(
                    HypershellCloudContract.RelativeSshCredentialsPath)),
            cancellationToken);
        EnsureSuccess(response, "list SSH credentials");
        var document = await ReadJsonAsync<SshCredentialList>(
            response,
            HypershellCloudContract.MaximumSshCredentialListBytes,
            cancellationToken);
        ValidateProtocol(document.ProtocolVersion);
        return document.Credentials;
    }

    public async Task<SshCredentialSummary> CreateCredentialAsync(
        SshCredentialMutationRequest credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        try
        {
            using var response = await SendJsonAsync(
                HttpMethod.Post,
                HypershellCloudContract.RelativeSshCredentialsPath,
                credential,
                cancellationToken);
            EnsureSuccess(response, "store SSH credential");
            return (await ReadJsonAsync<SshCredentialMutationReceipt>(
                response,
                64 * 1024,
                cancellationToken)).Credential;
        }
        finally
        {
            ClearCredentialMutation(credential);
        }
    }

    public async Task<SshCredentialSummary> RotateCredentialAsync(
        string credentialId,
        SshCredentialMutationRequest credential,
        CancellationToken cancellationToken = default)
    {
        ValidateId(credentialId, "SSH credential");
        ArgumentNullException.ThrowIfNull(credential);
        try
        {
            using var response = await SendJsonAsync(
                HttpMethod.Put,
                HypershellCloudContract.RelativeSshCredentialsPath
                + "/"
                + Uri.EscapeDataString(credentialId),
                credential,
                cancellationToken);
            EnsureSuccess(response, "rotate SSH credential");
            return (await ReadJsonAsync<SshCredentialMutationReceipt>(
                response,
                64 * 1024,
                cancellationToken)).Credential;
        }
        finally
        {
            ClearCredentialMutation(credential);
        }
    }

    public async Task RevokeCredentialAsync(
        string credentialId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(credentialId, "SSH credential");
        using var response = await SendAsync(
            new HttpRequestMessage(
                HttpMethod.Delete,
                CreateUri(
                    HypershellCloudContract.RelativeSshCredentialsPath
                    + "/"
                    + Uri.EscapeDataString(credentialId))),
            cancellationToken);
        EnsureSuccess(response, "revoke SSH credential");
    }

    public async Task<SshCredentialReleaseAuthorization>
        StartCredentialReleaseAsync(
            string credentialId,
            CancellationToken cancellationToken = default)
    {
        ValidateId(credentialId, "SSH credential");
        using var response = await SendAsync(
            new HttpRequestMessage(
                HttpMethod.Post,
                CreateUri(
                    HypershellCloudContract.RelativeSshCredentialsPath
                    + "/"
                    + Uri.EscapeDataString(credentialId)
                    + "/release")),
            cancellationToken);
        EnsureSuccess(response, "request SSH credential release");
        return await ReadJsonAsync<SshCredentialReleaseAuthorization>(
            response,
            32 * 1024,
            cancellationToken);
    }

    public async Task<SshCredentialReleaseClientResult>
        PollCredentialReleaseAsync(
            string releaseToken,
            CancellationToken cancellationToken = default)
    {
        using var response = await SendJsonAsync(
            HttpMethod.Post,
            HypershellCloudContract.RelativeCredentialReleasePath
            + "/token",
            new SshCredentialReleasePollRequest
            {
                ReleaseToken = releaseToken
            },
            cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new SshCredentialReleaseClientResult(
                SshCredentialReleaseClientKind.Approved,
                await ReadSecretJsonAsync(response, cancellationToken));
        }

        return new SshCredentialReleaseClientResult(
            response.StatusCode switch
            {
                HttpStatusCode.Accepted =>
                    SshCredentialReleaseClientKind.Pending,
                HttpStatusCode.Gone =>
                    SshCredentialReleaseClientKind.Expired,
                HttpStatusCode.Forbidden =>
                    SshCredentialReleaseClientKind.Denied,
                HttpStatusCode.NotFound =>
                    SshCredentialReleaseClientKind.Invalid,
                HttpStatusCode.Conflict =>
                    await IsChangedConflictAsync(
                        response,
                        cancellationToken)
                        ? SshCredentialReleaseClientKind.Changed
                        : SshCredentialReleaseClientKind.Consumed,
                _ => throw await CreateHttpFailureAsync(
                    response,
                    "poll SSH credential release",
                    cancellationToken)
            });
    }

    private async Task<HttpResponseMessage> SendJsonAsync<T>(
        HttpMethod method,
        string relativePath,
        T value,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        try
        {
            var request = new HttpRequestMessage(
                method,
                CreateUri(relativePath))
            {
                Content = new ByteArrayContent(payload)
            };
            request.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/json")
                {
                    CharSet = "utf-8"
                };
            return await SendAsync(request, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            return await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
        }
        catch (OperationCanceledException exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            throw Failure(
                ProfileSyncFailureKind.Timeout,
                "The linked-host request timed out.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw Failure(
                ProfileSyncFailureKind.Network,
                "The linked-host service could not be reached.",
                exception);
        }
        finally
        {
            request.Dispose();
        }
    }

    private static void EnsureSuccess(
        HttpResponseMessage response,
        string action)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw CreateStatusFailure(response.StatusCode, action);
    }

    private static ProfileSyncException CreateStatusFailure(
        HttpStatusCode statusCode,
        string action) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized => Failure(
                ProfileSyncFailureKind.Authentication,
                $"Sign in again to {action}."),
            HttpStatusCode.Forbidden => Failure(
                ProfileSyncFailureKind.Authorization,
                $"The account is not authorized to {action}."),
            HttpStatusCode.Conflict => Failure(
                ProfileSyncFailureKind.Conflict,
                $"The server state changed before Hypershell could {action}. Refresh and retry."),
            HttpStatusCode.NotFound => Failure(
                ProfileSyncFailureKind.NotFound,
                $"The requested resource was not found while trying to {action}."),
            _ => Failure(
                ProfileSyncFailureKind.Protocol,
                $"The server returned HTTP {(int)statusCode} while trying to {action}.")
        };

    private static async Task<ProfileSyncException> CreateHttpFailureAsync(
        HttpResponseMessage response,
        string action,
        CancellationToken cancellationToken)
    {
        try
        {
            var error = await ReadJsonAsync<CloudErrorDocument>(
                response,
                HypershellCloudContract.MaximumCloudErrorResponseBytes,
                cancellationToken);
            return new ProfileSyncException(
                response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized =>
                        ProfileSyncFailureKind.Authentication,
                    HttpStatusCode.Forbidden =>
                        ProfileSyncFailureKind.Authorization,
                    HttpStatusCode.Conflict =>
                        ProfileSyncFailureKind.Conflict,
                    HttpStatusCode.NotFound =>
                        ProfileSyncFailureKind.NotFound,
                    _ => ProfileSyncFailureKind.Protocol
                },
                string.IsNullOrWhiteSpace(error.Message)
                    ? $"The server could not {action}."
                    : error.Message);
        }
        catch (ProfileSyncException)
        {
            return CreateStatusFailure(response.StatusCode, action);
        }
    }

    private static async Task<bool> IsChangedConflictAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var error = await ReadJsonAsync<CloudErrorDocument>(
                response,
                HypershellCloudContract.MaximumCloudErrorResponseBytes,
                cancellationToken);
            return string.Equals(
                error.Error,
                "credential_release_changed",
                StringComparison.Ordinal);
        }
        catch (ProfileSyncException)
        {
            return false;
        }
    }

    private static async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var payload = await ReadBytesAsync(
            response,
            maximumBytes,
            cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<T>(payload, JsonOptions)
                   ?? throw Failure(
                       ProfileSyncFailureKind.Protocol,
                       "The linked-host response was empty.");
        }
        catch (JsonException exception)
        {
            throw Failure(
                ProfileSyncFailureKind.Protocol,
                "The linked-host response JSON was invalid.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static async Task<SshCredentialReleaseResponse>
        ReadSecretJsonAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
    {
        var credential = await ReadJsonAsync<SshCredentialReleaseResponse>(
            response,
            HypershellCloudContract.MaximumCredentialReleaseResponseBytes,
            cancellationToken);
        ValidateProtocol(credential.ProtocolVersion);
        if (credential.PrivateKeyUtf8.Length == 0
            || credential.PrivateKeyUtf8.Length > 1024 * 1024
            || credential.PassphraseUtf8.Length > 16 * 1024)
        {
            ClearReleasedCredential(credential);
            throw Failure(
                ProfileSyncFailureKind.Protocol,
                "The released SSH credential exceeded its bounds.");
        }

        return credential;
    }

    private static async Task<byte[]> ReadBytesAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > 0
            && response.Content.Headers.ContentLength > maximumBytes)
        {
            throw Failure(
                ProfileSyncFailureKind.Protocol,
                "The linked-host response exceeds its size limit.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (destination.Length + read > maximumBytes)
                {
                    throw Failure(
                        ProfileSyncFailureKind.Protocol,
                        "The linked-host response exceeds its size limit.");
                }

                destination.Write(buffer, 0, read);
            }

            return destination.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private Uri CreateUri(string relativePath) =>
        new(_endpoint, relativePath);

    private static void ValidateProtocol(int version)
    {
        if (version != HypershellCloudContract.CurrentProtocolVersion)
        {
            throw Failure(
                ProfileSyncFailureKind.Protocol,
                $"Cloud protocol version {version} is unsupported.");
        }
    }

    private static void ValidateId(string value, string label)
    {
        if (value.Length != 32 || !value.All(char.IsAsciiHexDigit))
        {
            throw Failure(
                ProfileSyncFailureKind.Configuration,
                $"The {label} ID is malformed.");
        }
    }

    private static void ValidateAccessToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)
            || accessToken.Length > 8192
            || accessToken.Any(character => character is '\r' or '\n'))
        {
            throw Failure(
                ProfileSyncFailureKind.Authentication,
                "A valid cloud session is required for linked hosts.");
        }
    }

    private static void ClearCredentialMutation(
        SshCredentialMutationRequest credential)
    {
        CryptographicOperations.ZeroMemory(credential.PrivateKeyUtf8);
        CryptographicOperations.ZeroMemory(credential.PassphraseUtf8);
    }

    public static void ClearReleasedCredential(
        SshCredentialReleaseResponse credential)
    {
        CryptographicOperations.ZeroMemory(credential.PrivateKeyUtf8);
        CryptographicOperations.ZeroMemory(credential.PassphraseUtf8);
    }

    private static ProfileSyncException Failure(
        ProfileSyncFailureKind kind,
        string message,
        Exception? innerException = null) =>
        new(kind, message, innerException);

    private static string GetApplicationVersion() =>
        typeof(HttpLinkedHostClient).Assembly.GetName().Version?.ToString()
        ?? "development";

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _client.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private sealed record CloudErrorDocument
    {
        public string Error { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;
    }
}
