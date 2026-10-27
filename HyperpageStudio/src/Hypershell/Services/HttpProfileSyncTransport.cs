using Hypershell.Profiles;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

public sealed class HttpProfileSyncTransport : IProfileSyncTransport
{
    private static readonly Regex ProfileIdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$",
        RegexOptions.CultureInvariant);

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

    private readonly HttpClient _httpClient;
    private readonly Uri _profilesUri;
    private readonly TimeSpan _requestTimeout;
    private bool _isDisposed;

    public HttpProfileSyncTransport(
        string serverEndpoint,
        string accessToken,
        HttpMessageHandler? handler = null,
        TimeSpan? requestTimeout = null)
    {
        var endpoint = ProfileSyncEndpoint.Normalize(serverEndpoint);
        ValidateAccessToken(accessToken);
        _profilesUri = new Uri(
            endpoint,
            ProfileSyncContract.RelativeProfilesPath);
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(20);
        if (_requestTimeout <= TimeSpan.Zero
            || _requestTimeout > TimeSpan.FromMinutes(2))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                "The profile-sync request timeout must be between zero and two minutes.");
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
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"Hypershell/{GetApplicationVersion()}");
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<RemoteProfileSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _profilesUri);
        using var response = await SendAsync(request, cancellationToken);
        EnsureSuccess(response, "list remote profiles");
        var payload = await ReadBoundedAsync(
            response.Content,
            ProfileSyncContract.MaximumListResponseBytes,
            cancellationToken);
        ProfileSyncListDocument document;
        try
        {
            document = JsonSerializer.Deserialize<ProfileSyncListDocument>(
                           payload.Span,
                           JsonOptions)
                       ?? throw new JsonException("The list response was empty.");
        }
        catch (JsonException exception)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                $"The server returned an invalid profile list: {exception.Message}",
                exception);
        }

        ValidateList(document);
        return document.Profiles
            .OrderBy(profile => profile.IsDeleted)
            .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ProfileSyncFetchResult> FetchAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            CreateProfileUri(profileId));
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                ProfileSyncContract.ProfileMediaType));
        using var response = await SendAsync(request, cancellationToken);
        EnsureSuccess(response, $"download profile '{profileId}'");
        var eTag = GetRequiredStrongETag(response);
        var serverRevision = GetRequiredServerRevision(response);
        var payload = await ReadBoundedAsync(
            response.Content,
            ProfileContract.MaximumBundleBytes,
            cancellationToken);
        return new ProfileSyncFetchResult(
            profileId,
            eTag,
            serverRevision,
            payload);
    }

    public async Task<ProfileSyncMutationResult> PutAsync(
        string profileId,
        ReadOnlyMemory<byte> profilePayload,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);
        if (profilePayload.Length is <= 0
            || profilePayload.Length > ProfileContract.MaximumBundleBytes)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                "Only a non-empty portable profile within the 4 MiB limit can be uploaded.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            CreateProfileUri(profileId));
        request.Content = new ReadOnlyMemoryContent(profilePayload);
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue(ProfileSyncContract.ProfileMediaType)
            {
                CharSet = "utf-8"
            };
        ApplyConditionalHeader(request, expectedETag);
        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Conflict
            or HttpStatusCode.PreconditionFailed)
        {
            return new ProfileSyncMutationResult(
                ProfileSyncMutationStatus.Conflict);
        }

        EnsureSuccess(response, $"upload profile '{profileId}'");
        var profile = await ReadMutationReceiptAsync(response, cancellationToken);
        return new ProfileSyncMutationResult(
            response.StatusCode == HttpStatusCode.Created
                ? ProfileSyncMutationStatus.Created
                : ProfileSyncMutationStatus.Updated,
            profile);
    }

    public async Task<ProfileSyncMutationResult> DeleteAsync(
        string profileId,
        string expectedETag,
        CancellationToken cancellationToken = default)
    {
        ValidateProfileId(profileId);
        ValidateStrongETag(expectedETag, "delete");
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            CreateProfileUri(profileId));
        request.Headers.TryAddWithoutValidation("If-Match", expectedETag);
        using var response = await SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Conflict
            or HttpStatusCode.PreconditionFailed)
        {
            return new ProfileSyncMutationResult(
                ProfileSyncMutationStatus.Conflict);
        }

        EnsureSuccess(response, $"delete remote profile '{profileId}'");
        var profile = await ReadMutationReceiptAsync(response, cancellationToken);
        if (!profile.IsDeleted)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                "The delete response did not contain a tombstone.");
        }

        return new ProfileSyncMutationResult(
            ProfileSyncMutationStatus.Deleted,
            profile);
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
        ObjectDisposedException.ThrowIf(_isDisposed, this);
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
                    "Profile sync refused an HTTP redirect. Configure the final HTTPS endpoint directly.");
            }

            return response;
        }
        catch (OperationCanceledException exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Timeout,
                "The profile-sync request timed out.",
                exception);
        }
        catch (OperationCanceledException exception)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Cancelled,
                "The profile-sync request was cancelled.",
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
            HttpStatusCode.NotFound or HttpStatusCode.Gone =>
                ProfileSyncFailureKind.NotFound,
            HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed =>
                ProfileSyncFailureKind.Conflict,
            _ when (int)response.StatusCode >= 500 =>
                ProfileSyncFailureKind.RemoteError,
            _ => ProfileSyncFailureKind.Protocol
        };
        throw new ProfileSyncException(
            kind,
            $"Unable to {operation}: server returned {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    private static async Task<RemoteProfileSummary> ReadMutationReceiptAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var responseETag = GetRequiredStrongETag(response);
        var responseRevision = GetRequiredServerRevision(response);
        var payload = await ReadBoundedAsync(
            response.Content,
            ProfileSyncContract.MaximumReceiptBytes,
            cancellationToken);
        ProfileSyncMutationReceipt receipt;
        try
        {
            receipt = JsonSerializer.Deserialize<ProfileSyncMutationReceipt>(
                          payload.Span,
                          JsonOptions)
                      ?? throw new JsonException("The mutation receipt was empty.");
        }
        catch (JsonException exception)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                $"The server returned an invalid mutation receipt: {exception.Message}",
                exception);
        }

        ValidateProtocolVersion(receipt.ProtocolVersion);
        ValidateSummary(receipt.Profile);
        if (!string.Equals(
                receipt.Profile.ETag,
                responseETag,
                StringComparison.Ordinal))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                "The response ETag did not match the mutation receipt.");
        }

        if (receipt.Profile.ServerRevision != responseRevision)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                $"The {ProfileSyncContract.ServerRevisionHeader} response header did not match the mutation receipt.");
        }

        return receipt.Profile;
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
                $"The profile server response exceeded the {maximumBytes:N0}-byte limit.");
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
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
                throw new ProfileSyncException(
                    ProfileSyncFailureKind.Protocol,
                    $"The profile server response exceeded the {maximumBytes:N0}-byte limit.");
            }

            destination.Write(buffer, 0, read);
        }

        return destination.ToArray();
    }

    private void ApplyConditionalHeader(
        HttpRequestMessage request,
        string? expectedETag)
    {
        if (string.IsNullOrWhiteSpace(expectedETag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", "*");
            return;
        }

        ValidateStrongETag(expectedETag, "upload");
        request.Headers.TryAddWithoutValidation("If-Match", expectedETag);
    }

    private Uri CreateProfileUri(string profileId) =>
        new($"{_profilesUri.AbsoluteUri.TrimEnd('/')}/{Uri.EscapeDataString(profileId)}");

    private static string GetRequiredStrongETag(HttpResponseMessage response)
    {
        var eTag = response.Headers.ETag?.ToString() ?? string.Empty;
        ValidateStrongETag(eTag, "response");
        return eTag;
    }

    private static long GetRequiredServerRevision(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(
                ProfileSyncContract.ServerRevisionHeader,
                out var values)
            || !long.TryParse(
                values.SingleOrDefault(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var revision)
            || revision < 1)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                $"The server response did not include a valid {ProfileSyncContract.ServerRevisionHeader} header.");
        }

        return revision;
    }

    private static void ValidateList(ProfileSyncListDocument document)
    {
        ValidateProtocolVersion(document.ProtocolVersion);
        if (document.Profiles.Count > ProfileSyncContract.MaximumRemoteProfiles)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                $"The server returned more than {ProfileSyncContract.MaximumRemoteProfiles:N0} profile records.");
        }

        var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in document.Profiles)
        {
            ValidateSummary(profile);
            if (!profileIds.Add(profile.Id))
            {
                throw new ProfileSyncException(
                    ProfileSyncFailureKind.Protocol,
                    $"The server returned duplicate profile ID '{profile.Id}'.");
            }
        }
    }

    private static void ValidateSummary(RemoteProfileSummary profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateProfileId(profile.Id);
        if (string.IsNullOrWhiteSpace(profile.DisplayName)
            || profile.DisplayName.Length > 120
            || profile.ServerRevision < 1
            || profile.ProfileRevision < 0
            || profile.UpdatedAtUtc == default
            || profile.ProfileSha256.Length != 64
            || !profile.ProfileSha256.All(Uri.IsHexDigit))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                $"The server returned invalid metadata for profile '{profile.Id}'.");
        }

        ValidateStrongETag(profile.ETag, "profile metadata");
    }

    private static void ValidateProtocolVersion(int version)
    {
        if (version != ProfileSyncContract.CurrentProtocolVersion)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                $"Profile-sync protocol version {version} is not supported by this build.");
        }
    }

    private static void ValidateProfileId(string profileId)
    {
        if (!ProfileIdPattern.IsMatch(profileId ?? string.Empty))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                "The remote profile ID is empty or malformed.");
        }
    }

    private static void ValidateStrongETag(string eTag, string operation)
    {
        if (string.IsNullOrWhiteSpace(eTag)
            || eTag.StartsWith("W/", StringComparison.OrdinalIgnoreCase)
            || eTag.Length is < 3 or > 258
            || eTag[0] != '"'
            || eTag[^1] != '"'
            || eTag.Any(character => char.IsControl(character)))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                $"A strong quoted ETag is required for profile {operation}.");
        }
    }

    private static void ValidateAccessToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)
            || accessToken.Length > 8192
            || accessToken.Any(character => character is '\r' or '\n'))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Authentication,
                "Enter a valid session access token. Hypershell will not save it to disk.");
        }
    }

    private static string GetApplicationVersion() =>
        typeof(HttpProfileSyncTransport).Assembly.GetName().Version?.ToString()
        ?? "development";
}

public static class ProfileSyncEndpoint
{
    public const string ProductionEndpoint =
        "https://hypershell.atlasuniversalis.com/";

    public static Uri Normalize(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var endpoint)
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                "Enter an absolute server URL without credentials, a query, or a fragment.");
        }

        var isHttps = string.Equals(
            endpoint.Scheme,
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase);
        var isLoopbackHttp = string.Equals(
                                 endpoint.Scheme,
                                 Uri.UriSchemeHttp,
                                 StringComparison.OrdinalIgnoreCase)
                             && endpoint.IsLoopback;
        if (!isHttps && !isLoopbackHttp)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                "Profile sync requires HTTPS. HTTP is allowed only for a loopback development server.");
        }

        var builder = new UriBuilder(endpoint)
        {
            Path = endpoint.AbsolutePath.TrimEnd('/') + "/"
        };
        return builder.Uri;
    }

    public static string CreateEndpointKey(string value)
    {
        var endpoint = Normalize(value);
        return endpoint.AbsoluteUri.TrimEnd('/');
    }
}
