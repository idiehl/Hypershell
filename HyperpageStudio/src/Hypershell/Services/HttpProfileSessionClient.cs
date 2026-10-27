using Hypershell.Profiles;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

public interface IProfileSessionClient : IAsyncDisposable
{
    Task<ProfileSessionResponse> SignInAsync(
        string accountId,
        string accessKey,
        CancellationToken cancellationToken = default);

    Task SignOutAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
}

public sealed partial class HttpProfileSessionClient
    : IProfileSessionClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 16,
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
    private readonly Uri _sessionUri;
    private readonly TimeSpan _requestTimeout;
    private bool _isDisposed;

    public HttpProfileSessionClient(
        string serverEndpoint,
        HttpMessageHandler? handler = null,
        TimeSpan? requestTimeout = null)
    {
        var endpoint = ProfileSyncEndpoint.Normalize(serverEndpoint);
        _sessionUri = new Uri(
            endpoint,
            ProfileSessionContract.RelativeSessionPath);
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(20);
        if (_requestTimeout <= TimeSpan.Zero
            || _requestTimeout > TimeSpan.FromMinutes(2))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                "The profile sign-in timeout must be between zero and two minutes.");
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

    public async Task<ProfileSessionResponse> SignInAsync(
        string accountId,
        string accessKey,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var normalizedAccountId = accountId?.Trim() ?? string.Empty;
        if (!AccountIdPattern().IsMatch(normalizedAccountId))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                "Enter the 3-64 character account ID provisioned by the profile server administrator.");
        }

        if (string.IsNullOrWhiteSpace(accessKey)
            || accessKey.Length > 256
            || accessKey.Any(char.IsControl))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Authentication,
                "Enter a valid account access key.");
        }

        var requestDocument = new ProfileSessionRequest
        {
            AccountId = normalizedAccountId,
            AccessKey = accessKey
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            requestDocument,
            JsonOptions);
        if (payload.LongLength > ProfileSessionContract.MaximumSignInRequestBytes)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                "The profile sign-in request exceeds its protocol limit.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _sessionUri)
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json")
            {
                CharSet = "utf-8"
            };
        using var response = await SendAsync(request, cancellationToken);
        EnsureSuccess(response, "sign in to profile sync");
        var responsePayload = await ReadBoundedAsync(
            response.Content,
            ProfileSessionContract.MaximumSessionResponseBytes,
            cancellationToken);
        ProfileSessionResponse session;
        try
        {
            session = JsonSerializer.Deserialize<ProfileSessionResponse>(
                          responsePayload.Span,
                          JsonOptions)
                      ?? throw new JsonException(
                          "The profile session response was empty.");
        }
        catch (JsonException exception)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                $"The profile server returned an invalid sign-in response: {exception.Message}",
                exception);
        }

        ValidateSession(session, normalizedAccountId);
        return session;
    }

    public async Task SignOutAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ValidateSessionToken(accessToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            _sessionUri);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden)
        {
            return;
        }

        EnsureSuccess(response, "sign out of profile sync");
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
                    "Profile sign-in refused an HTTP redirect. Configure the final HTTPS endpoint directly.");
            }

            return response;
        }
        catch (OperationCanceledException exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Timeout,
                "The profile sign-in request timed out.",
                exception);
        }
        catch (OperationCanceledException exception)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Cancelled,
                "The profile sign-in request was cancelled.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Network,
                $"The profile server could not be reached: {exception.Message}",
                exception);
        }
    }

    private static void EnsureSuccess(
        HttpResponseMessage response,
        string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

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
        throw new ProfileSyncException(
            kind,
            $"Unable to {operation}: server returned {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    private static async Task<ReadOnlyMemory<byte>> ReadBoundedAsync(
        HttpContent content,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0
            && content.Headers.ContentLength > maximumBytes)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                "The profile session response exceeded its protocol limit.");
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
                    "The profile session response exceeded its protocol limit.");
            }

            destination.Write(buffer, 0, read);
        }

        return destination.ToArray();
    }

    private static void ValidateSession(
        ProfileSessionResponse session,
        string requestedAccountId)
    {
        if (session.ProtocolVersion
            != ProfileSessionContract.CurrentProtocolVersion
            || !string.Equals(
                session.AccountId,
                requestedAccountId,
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(session.AccountDisplayName)
            || session.AccountDisplayName.Length > 120
            || session.ExpiresAtUtc <= DateTimeOffset.UtcNow
            || session.ExpiresAtUtc > DateTimeOffset.UtcNow.AddHours(24))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                "The profile server returned mismatched account or session metadata.");
        }

        ValidateSessionToken(session.AccessToken);
    }

    private static void ValidateSessionToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)
            || accessToken.Length > 8192
            || accessToken.Any(character => character is '\r' or '\n'))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                "The profile server returned a malformed session token.");
        }
    }

    private static string GetApplicationVersion() =>
        typeof(HttpProfileSessionClient).Assembly.GetName().Version?.ToString()
        ?? "development";
}
