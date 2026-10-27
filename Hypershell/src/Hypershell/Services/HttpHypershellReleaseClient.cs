using Hypershell.Profiles;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Hypershell.Services;

public interface IHypershellReleaseClient : IAsyncDisposable
{
    Task<SignedHypershellReleaseManifest> GetLatestAsync(
        string channel,
        string accessToken,
        CancellationToken cancellationToken = default);

    Task DownloadArtifactAsync(
        string version,
        HypershellReleaseArtifact artifact,
        string accessToken,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class HttpHypershellReleaseClient
    : IHypershellReleaseClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _releasesUri;
    private readonly HypershellReleaseManifestSecurity _security = new();
    private readonly TimeSpan _requestTimeout;
    private bool _isDisposed;

    public HttpHypershellReleaseClient(
        string endpoint,
        HttpMessageHandler? handler = null,
        TimeSpan? requestTimeout = null)
    {
        _releasesUri = new Uri(
            ProfileSyncEndpoint.Normalize(endpoint),
            HypershellCloudContract.RelativeReleasesPath + "/");
        _requestTimeout = requestTimeout ?? TimeSpan.FromMinutes(5);
        if (_requestTimeout < TimeSpan.FromSeconds(5)
            || _requestTimeout > TimeSpan.FromMinutes(30))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                "Release requests must time out between five seconds and 30 minutes.");
        }

        handler ??= new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression =
                DecompressionMethods.GZip
                | DecompressionMethods.Deflate
                | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"Hypershell/{GetApplicationVersion()}");
    }

    public async Task<SignedHypershellReleaseManifest> GetLatestAsync(
        string channel,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ValidateAccessToken(accessToken);
        var normalizedChannel = NormalizeChannel(channel);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(
                _releasesUri,
                "latest?channel="
                + Uri.EscapeDataString(normalizedChannel)));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await SendAsync(request, cancellationToken);
        EnsureSuccess(response, "check for updates");
        if (!IsJson(response.Content.Headers.ContentType?.MediaType))
        {
            throw ProtocolFailure(
                "The update service returned a non-JSON release manifest.");
        }

        var bytes = await ReadBoundedAsync(
            response.Content,
            HypershellReleaseContract.MaximumSignedManifestBytes,
            cancellationToken);
        try
        {
            return _security.DeserializeEnvelope(bytes.Span);
        }
        catch (InvalidDataException exception)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                exception.Message,
                exception);
        }
    }

    public async Task DownloadArtifactAsync(
        string version,
        HypershellReleaseArtifact artifact,
        string accessToken,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ValidateAccessToken(accessToken);
        ArgumentNullException.ThrowIfNull(artifact);
        var destination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(destination)
            ?? throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                "The update destination has no parent directory."));
        var temporary = destination
                        + ".download-"
                        + Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(
                _releasesUri,
                Uri.EscapeDataString(version)
                + "/artifacts/"
                + artifact.Kind.ToString().ToLowerInvariant()));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            using var response = await SendAsync(request, cancellationToken);
            EnsureSuccess(response, "download the update");
            if (response.Content.Headers.ContentLength is { } contentLength
                && contentLength != artifact.ByteLength)
            {
                throw ProtocolFailure(
                    "The update artifact length did not match its signed manifest.");
            }

            long total = 0;
            string sha256;
            await using (var source =
                         await response.Content.ReadAsStreamAsync(
                             cancellationToken))
            await using (var target = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             FileOptions.Asynchronous
                             | FileOptions.WriteThrough))
            using (var hash = IncrementalHash.CreateHash(
                       HashAlgorithmName.SHA256))
            {
                var buffer = new byte[128 * 1024];
                while (true)
                {
                    var read = await source.ReadAsync(
                        buffer,
                        cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    total = checked(total + read);
                    if (total > artifact.ByteLength)
                    {
                        throw ProtocolFailure(
                            "The update artifact exceeded its signed length.");
                    }

                    hash.AppendData(buffer.AsSpan(0, read));
                    await target.WriteAsync(
                        buffer.AsMemory(0, read),
                        cancellationToken);
                    progress?.Report(
                        artifact.ByteLength == 0
                            ? 0
                            : (double)total / artifact.ByteLength);
                }

                await target.FlushAsync(cancellationToken);
                target.Flush(flushToDisk: true);
                sha256 = Convert.ToHexString(hash.GetHashAndReset())
                    .ToLowerInvariant();
            }

            if (total != artifact.ByteLength
                || !string.Equals(
                    sha256,
                    artifact.Sha256,
                    StringComparison.Ordinal))
            {
                throw ProtocolFailure(
                    "The downloaded update failed its signed size or SHA-256 check.");
            }

            File.Move(temporary, destination, overwrite: false);
            progress?.Report(1);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
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
                throw ProtocolFailure(
                    "The update service refused an HTTP redirect.");
            }

            return response;
        }
        catch (OperationCanceledException exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Timeout,
                "The update request timed out.",
                exception);
        }
        catch (OperationCanceledException exception)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Cancelled,
                "The update request was cancelled.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Network,
                $"The update service could not be reached: {exception.Message}",
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
            HttpStatusCode.NotFound =>
                ProfileSyncFailureKind.NotFound,
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
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0
            && content.Headers.ContentLength > maximumBytes)
        {
            throw ProtocolFailure(
                "The signed release manifest exceeded its protocol limit.");
        }

        await using var source = await content.ReadAsStreamAsync(
            cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > maximumBytes)
            {
                throw ProtocolFailure(
                    "The signed release manifest exceeded its protocol limit.");
            }

            destination.Write(buffer, 0, read);
        }

        return destination.ToArray();
    }

    private static void ValidateAccessToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)
            || !accessToken.StartsWith("hps_", StringComparison.Ordinal)
            || accessToken.Length is < 40 or > 8192
            || accessToken.Any(character => character is '\r' or '\n'))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Authentication,
                "A valid account session is required to access releases.");
        }
    }

    private static string NormalizeChannel(string channel)
    {
        var normalized = channel?.Trim().ToLowerInvariant()
                         ?? string.Empty;
        if (normalized.Length is < 2 or > 32
            || !normalized.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character == '-'))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                "The update channel is malformed.");
        }

        return normalized;
    }

    private static bool IsJson(string? mediaType) =>
        string.Equals(
            mediaType,
            "application/json",
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            mediaType,
            "application/problem+json",
            StringComparison.OrdinalIgnoreCase);

    private static ProfileSyncException ProtocolFailure(string message) =>
        new(ProfileSyncFailureKind.Protocol, message);

    private static string GetApplicationVersion() =>
        typeof(HttpHypershellReleaseClient)
            .Assembly
            .GetName()
            .Version
            ?.ToString()
        ?? "development";
}
