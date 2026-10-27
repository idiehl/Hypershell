using Hypershell.Profiles;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

public enum CloudDeviceAuthorizationPollKind
{
    Pending,
    Approved,
    Denied,
    Expired,
    Consumed,
    Invalid
}

public sealed record CloudDeviceAuthorizationPollResult(
    CloudDeviceAuthorizationPollKind Kind,
    CloudSessionResponse? Session = null);

public interface ICloudDeviceAuthorizationClient : IAsyncDisposable
{
    Task<DeviceAuthorizationResponse> StartAsync(
        DeviceAuthorizationRequest request,
        CancellationToken cancellationToken = default);

    Task<CloudDeviceAuthorizationPollResult> PollAsync(
        string deviceCode,
        CancellationToken cancellationToken = default);

    Task<CloudSessionResponse> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);

    Task RevokeDeviceAsync(
        string accessToken,
        string clientDeviceId,
        CancellationToken cancellationToken = default);
}

public sealed partial class HttpCloudDeviceAuthorizationClient
    : ICloudDeviceAuthorizationClient
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

    [GeneratedRegex(
        "^[A-Za-z0-9][A-Za-z0-9._-]{2,63}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex AccountIdPattern();

    private readonly HttpClient _httpClient;
    private readonly Uri _deviceAuthorizationUri;
    private readonly Uri _deviceTokenUri;
    private readonly Uri _refreshTokenUri;
    private readonly Uri _clientDevicesUri;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _requestTimeout;
    private bool _isDisposed;

    public HttpCloudDeviceAuthorizationClient(
        string serverEndpoint,
        HttpMessageHandler? handler = null,
        TimeProvider? timeProvider = null,
        TimeSpan? requestTimeout = null)
    {
        var endpoint = ProfileSyncEndpoint.Normalize(serverEndpoint);
        _deviceAuthorizationUri = new Uri(
            endpoint,
            HypershellCloudContract.RelativeDeviceAuthorizationPath);
        _deviceTokenUri = new Uri(
            endpoint,
            HypershellCloudContract.RelativeDeviceTokenPath);
        _refreshTokenUri = new Uri(
            endpoint,
            HypershellCloudContract.RelativeRefreshTokenPath);
        _clientDevicesUri = new Uri(
            endpoint,
            HypershellCloudContract.RelativeClientDevicesPath + "/");
        _timeProvider = timeProvider ?? TimeProvider.System;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(20);
        if (_requestTimeout <= TimeSpan.Zero
            || _requestTimeout > TimeSpan.FromMinutes(2))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                "Cloud account requests must time out between zero and two minutes.");
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
        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"Hypershell/{GetApplicationVersion()}");
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<DeviceAuthorizationResponse> StartAsync(
        DeviceAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(request);
        var payload = SerializeBounded(
            request,
            HypershellCloudContract.MaximumDeviceAuthorizationRequestBytes,
            "device authorization");
        using var message = CreateJsonPost(_deviceAuthorizationUri, payload);
        using var response = await SendAsync(message, cancellationToken);
        EnsureSuccess(response, "start device authorization");
        var challenge = await ReadJsonAsync<DeviceAuthorizationResponse>(
            response.Content,
            HypershellCloudContract.MaximumDeviceAuthorizationResponseBytes,
            "device authorization",
            cancellationToken);
        ValidateChallenge(challenge);
        return challenge;
    }

    public async Task<CloudDeviceAuthorizationPollResult> PollAsync(
        string deviceCode,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ValidateOpaqueToken(deviceCode, "hsd_", "device code");
        var payload = SerializeBounded(
            new DeviceTokenRequest { DeviceCode = deviceCode },
            HypershellCloudContract.MaximumDeviceTokenRequestBytes,
            "device-token poll");
        using var message = CreateJsonPost(_deviceTokenUri, payload);
        using var response = await SendAsync(message, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            return new CloudDeviceAuthorizationPollResult(
                CloudDeviceAuthorizationPollKind.Pending);
        }

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var session = await ReadJsonAsync<CloudSessionResponse>(
                response.Content,
                HypershellCloudContract.MaximumCloudSessionResponseBytes,
                "cloud session",
                cancellationToken);
            ValidateSession(session);
            return new CloudDeviceAuthorizationPollResult(
                CloudDeviceAuthorizationPollKind.Approved,
                session);
        }

        return response.StatusCode switch
        {
            HttpStatusCode.Forbidden => new(
                CloudDeviceAuthorizationPollKind.Denied),
            HttpStatusCode.Gone => new(
                CloudDeviceAuthorizationPollKind.Expired),
            HttpStatusCode.Conflict => new(
                CloudDeviceAuthorizationPollKind.Consumed),
            HttpStatusCode.BadRequest => new(
                CloudDeviceAuthorizationPollKind.Invalid),
            _ => throw CreateFailure(
                response,
                "poll device authorization")
        };
    }

    public async Task<CloudSessionResponse> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ValidateOpaqueToken(refreshToken, "hsr_", "refresh credential");
        var payload = SerializeBounded(
            new RefreshTokenRequest { RefreshToken = refreshToken },
            HypershellCloudContract.MaximumDeviceTokenRequestBytes,
            "session refresh");
        using var message = CreateJsonPost(_refreshTokenUri, payload);
        using var response = await SendAsync(message, cancellationToken);
        EnsureSuccess(response, "refresh the cloud session");
        var session = await ReadJsonAsync<CloudSessionResponse>(
            response.Content,
            HypershellCloudContract.MaximumCloudSessionResponseBytes,
            "cloud session",
            cancellationToken);
        ValidateSession(session);
        return session;
    }

    public async Task RevokeDeviceAsync(
        string accessToken,
        string clientDeviceId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ValidateOpaqueToken(accessToken, "hps_", "access token");
        ValidateDeviceId(clientDeviceId);
        using var message = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri(
                _clientDevicesUri,
                Uri.EscapeDataString(clientDeviceId)));
        message.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await SendAsync(message, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        EnsureSuccess(response, "revoke the client device");
    }

    public static Uri CreateCompleteVerificationUri(
        DeviceAuthorizationResponse challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        var builder = new UriBuilder(challenge.VerificationUri);
        builder.Query =
            "userCode=" + Uri.EscapeDataString(challenge.UserCode);
        return builder.Uri;
    }

    public ValueTask DisposeAsync()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _httpClient.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        try
        {
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                response.Dispose();
                throw new ProfileSyncException(
                    ProfileSyncFailureKind.Protocol,
                    "The Hypershell account service refused an HTTP redirect. Configure the final HTTPS endpoint directly.");
            }

            return response;
        }
        catch (OperationCanceledException exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Timeout,
                "The Hypershell account request timed out.",
                exception);
        }
        catch (OperationCanceledException exception)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Cancelled,
                "The Hypershell account request was cancelled.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Network,
                $"The Hypershell account service could not be reached: {exception.Message}",
                exception);
        }
    }

    private static HttpRequestMessage CreateJsonPost(
        Uri uri,
        byte[] payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8"
            };
        return request;
    }

    private static byte[] SerializeBounded<T>(
        T document,
        int maximumBytes,
        string operation)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            document,
            JsonOptions);
        if (payload.LongLength > maximumBytes)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                $"The {operation} request exceeds its protocol limit.");
        }

        return payload;
    }

    private static async Task<T> ReadJsonAsync<T>(
        HttpContent content,
        int maximumBytes,
        string documentName,
        CancellationToken cancellationToken)
    {
        var payload = await ReadBoundedAsync(
            content,
            maximumBytes,
            cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<T>(payload.Span, JsonOptions)
                   ?? throw new JsonException(
                       $"The {documentName} response was empty.");
        }
        catch (JsonException exception)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                $"The account service returned an invalid {documentName} response: {exception.Message}",
                exception);
        }
    }

    private static async Task<ReadOnlyMemory<byte>> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0
            && content.Headers.ContentLength > maximumBytes)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                "The account-service response exceeded its protocol limit.");
        }

        await using var source = await content.ReadAsStreamAsync(
            cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[8 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new ProfileSyncException(
                    ProfileSyncFailureKind.Protocol,
                    "The account-service response exceeded its protocol limit.");
            }

            destination.Write(buffer, 0, read);
        }

        return destination.ToArray();
    }

    private static void EnsureSuccess(
        HttpResponseMessage response,
        string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw CreateFailure(response, operation);
        }
    }

    private static ProfileSyncException CreateFailure(
        HttpResponseMessage response,
        string operation)
    {
        var kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                ProfileSyncFailureKind.Authentication,
            HttpStatusCode.Forbidden =>
                ProfileSyncFailureKind.Authorization,
            HttpStatusCode.TooManyRequests =>
                ProfileSyncFailureKind.RemoteError,
            _ when (int)response.StatusCode >= 500 =>
                ProfileSyncFailureKind.RemoteError,
            _ => ProfileSyncFailureKind.Protocol
        };
        return new ProfileSyncException(
            kind,
            $"Unable to {operation}: server returned {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    private void ValidateChallenge(DeviceAuthorizationResponse challenge)
    {
        var now = _timeProvider.GetUtcNow();
        if (challenge.ProtocolVersion
            != HypershellCloudContract.CurrentProtocolVersion
            || !IsOpaqueToken(challenge.DeviceCode, "hsd_")
            || challenge.UserCode.Length != 9
            || challenge.UserCode[4] != '-'
            || challenge.UserCode.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character != '-')
            || !Uri.TryCreate(
                challenge.VerificationUri,
                UriKind.Absolute,
                out var verificationUri)
            || (!string.Equals(
                    verificationUri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase)
                && !(verificationUri.IsLoopback
                     && string.Equals(
                         verificationUri.Scheme,
                         Uri.UriSchemeHttp,
                         StringComparison.OrdinalIgnoreCase)))
            || challenge.ExpiresAtUtc <= now
            || challenge.ExpiresAtUtc > now.AddMinutes(20)
            || challenge.PollIntervalSeconds is < 2 or > 30)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                "The account service returned malformed device-authorization metadata.");
        }
    }

    private void ValidateSession(CloudSessionResponse session)
    {
        var now = _timeProvider.GetUtcNow();
        if (session.ProtocolVersion
            != HypershellCloudContract.CurrentProtocolVersion
            || !AccountIdPattern().IsMatch(session.AccountId)
            || string.IsNullOrWhiteSpace(session.AccountDisplayName)
            || session.AccountDisplayName.Length > 120
            || !IsDeviceId(session.ClientDeviceId)
            || !IsOpaqueToken(session.AccessToken, "hps_")
            || !IsOpaqueToken(session.RefreshToken, "hsr_")
            || session.AccessTokenExpiresAtUtc <= now
            || session.AccessTokenExpiresAtUtc > now.AddHours(24)
            || session.RefreshTokenExpiresAtUtc <=
               session.AccessTokenExpiresAtUtc
            || session.RefreshTokenExpiresAtUtc > now.AddDays(90))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                "The account service returned malformed session or client-device metadata.");
        }
    }

    private static void ValidateOpaqueToken(
        string value,
        string prefix,
        string field)
    {
        if (!IsOpaqueToken(value, prefix))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                $"The {field} is malformed.");
        }
    }

    private static bool IsOpaqueToken(string value, string prefix) =>
        !string.IsNullOrWhiteSpace(value)
        && value.StartsWith(prefix, StringComparison.Ordinal)
        && value.Length is >= 40 and <= 8192
        && !value.Any(character => character is '\r' or '\n');

    private static void ValidateDeviceId(string value)
    {
        if (!IsDeviceId(value))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                "The client-device ID is malformed.");
        }
    }

    private static bool IsDeviceId(string value) =>
        value.Length == 32
        && value.All(char.IsAsciiHexDigit);

    private static string GetApplicationVersion() =>
        typeof(HttpCloudDeviceAuthorizationClient)
            .Assembly
            .GetName()
            .Version
            ?.ToString()
        ?? "development";
}
