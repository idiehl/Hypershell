using Hypershell.Profiles;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypershell.Services;

/// <summary>
/// Strict authenticated desktop client for v2 roaming adjunct resources. The
/// bounded v1 portable-profile transport remains separate for compatibility.
/// </summary>
public sealed class HttpCloudRoamingTransport : ICloudRoamingTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 64,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(
                namingPolicy: null,
                allowIntegerValues: false)
        }
    };

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly TimeSpan _requestTimeout;
    private bool _isDisposed;

    public HttpCloudRoamingTransport(
        string serverEndpoint,
        string accessToken,
        HttpMessageHandler? handler = null,
        TimeSpan? requestTimeout = null)
    {
        _endpoint = ProfileSyncEndpoint.Normalize(serverEndpoint);
        ValidateAccessToken(accessToken);
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(60);
        if (_requestTimeout <= TimeSpan.Zero
            || _requestTimeout > TimeSpan.FromMinutes(10))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Configuration,
                "The cloud-resource request timeout must be between zero and ten minutes.");
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

    public async Task<IReadOnlyList<WorkspaceProfileRoamingSummary>>
        ListWorkspaceProfilesAsync(
            CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                CreateUri(HypershellCloudContract.RelativeWorkspaceProfilesPath)),
            cancellationToken);
        EnsureSuccess(response, "list roaming workspace profiles");
        var document = await ReadJsonAsync<WorkspaceProfileRoamingList>(
            response,
            HypershellCloudContract.MaximumWorkspaceProfileListBytes,
            cancellationToken);
        ValidateProtocol(document.ProtocolVersion);
        foreach (var profile in document.Profiles)
        {
            ValidateWorkspaceSummary(profile);
        }

        return document.Profiles;
    }

    public async Task<WorkspaceProfileRoamingResource>
        GetWorkspaceProfileAsync(
            string profileId,
            CancellationToken cancellationToken = default)
    {
        ValidateResourceId(profileId, "workspace profile");
        using var response = await SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                WorkspaceProfileUri(profileId)),
            cancellationToken);
        EnsureSuccess(response, $"download roaming profile '{profileId}'");
        var resource = await ReadJsonAsync<WorkspaceProfileRoamingResource>(
            response,
            HypershellCloudContract.MaximumWorkspaceProfileBytes,
            cancellationToken);
        ValidateProtocol(resource.ProtocolVersion);
        ValidateWorkspaceSummary(resource.Profile);
        if (resource.Document is null
            || !string.Equals(
                resource.Document.ProfileId,
                profileId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                resource.Profile.ETag,
                GetRequiredStrongETag(response),
                StringComparison.Ordinal))
        {
            throw Protocol(
                "The roaming-profile response identity or ETag was inconsistent.");
        }

        return resource;
    }

    public async Task<WorkspaceProfileCloudMutationResult>
        PutWorkspaceProfileAsync(
            WorkspaceProfileRoamingDocument profile,
            string? expectedETag,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateResourceId(profile.ProfileId, "workspace profile");
        using var request = CreateJsonPut(
            WorkspaceProfileUri(profile.ProfileId),
            profile,
            expectedETag);
        using var response = await SendAsync(request, cancellationToken);
        if (IsConflict(response.StatusCode))
        {
            return new WorkspaceProfileCloudMutationResult(
                CloudMutationStatus.Conflict);
        }

        EnsureSuccess(response, $"upload roaming profile '{profile.ProfileId}'");
        var receipt = await ReadJsonAsync<
            WorkspaceProfileRoamingMutationReceipt>(
            response,
            64 * 1024,
            cancellationToken);
        ValidateProtocol(receipt.ProtocolVersion);
        ValidateWorkspaceSummary(receipt.Profile);
        ValidateReceiptHeaders(
            response,
            receipt.Profile.ETag,
            receipt.Profile.ServerRevision);
        return new WorkspaceProfileCloudMutationResult(
            response.StatusCode == HttpStatusCode.Created
                ? CloudMutationStatus.Created
                : CloudMutationStatus.Updated,
            receipt.Profile);
    }

    public async Task<WorkspaceProfileCloudMutationResult>
        DeleteWorkspaceProfileAsync(
            string profileId,
            string expectedETag,
            CancellationToken cancellationToken = default)
    {
        ValidateResourceId(profileId, "workspace profile");
        ValidateStrongETag(expectedETag);
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            WorkspaceProfileUri(profileId));
        request.Headers.TryAddWithoutValidation("If-Match", expectedETag);
        using var response = await SendAsync(request, cancellationToken);
        if (IsConflict(response.StatusCode))
        {
            return new WorkspaceProfileCloudMutationResult(
                CloudMutationStatus.Conflict);
        }

        EnsureSuccess(response, $"delete roaming profile '{profileId}'");
        var receipt = await ReadJsonAsync<
            WorkspaceProfileRoamingMutationReceipt>(
            response,
            64 * 1024,
            cancellationToken);
        ValidateWorkspaceSummary(receipt.Profile);
        if (!receipt.Profile.IsDeleted)
        {
            throw Protocol("The roaming-profile delete response was not a tombstone.");
        }

        return new WorkspaceProfileCloudMutationResult(
            CloudMutationStatus.Deleted,
            receipt.Profile);
    }

    public async Task<AiConversationListPage> ListConversationsAsync(
        string? profileId,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            ValidateResourceId(profileId, "workspace profile");
        }

        if (pageSize is < 1 or > 100)
        {
            throw Configuration("Conversation-list page size must be 1-100.");
        }

        var query = new List<string> { $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            query.Add("profileId=" + Uri.EscapeDataString(profileId));
        }

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            query.Add("cursor=" + Uri.EscapeDataString(cursor));
        }

        using var response = await SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                CreateUri(
                    HypershellCloudContract.RelativeConversationsPath
                    + "?"
                    + string.Join("&", query))),
            cancellationToken);
        EnsureSuccess(response, "list AI conversations");
        var page = await ReadJsonAsync<AiConversationListPage>(
            response,
            HypershellCloudContract.MaximumConversationPageBytes,
            cancellationToken);
        ValidateProtocol(page.ProtocolVersion);
        foreach (var conversation in page.Conversations)
        {
            ValidateConversationSummary(conversation);
        }

        return page;
    }

    public async Task<AiConversationPage> GetConversationPageAsync(
        string conversationId,
        long afterSequence,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ValidateResourceId(conversationId, "conversation");
        if (afterSequence < 0
            || pageSize is < 1
                or > HypershellCloudContract.MaximumConversationItemsPerPage)
        {
            throw Configuration("Conversation pagination values are invalid.");
        }

        using var response = await SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                ConversationUri(conversationId, afterSequence, pageSize)),
            cancellationToken);
        EnsureSuccess(response, $"download conversation '{conversationId}'");
        var page = await ReadJsonAsync<AiConversationPage>(
            response,
            HypershellCloudContract.MaximumConversationPageBytes,
            cancellationToken);
        ValidateProtocol(page.ProtocolVersion);
        ValidateConversationSummary(page.Conversation);
        if (!string.Equals(
                page.Conversation.Id,
                conversationId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                page.Conversation.ETag,
                GetRequiredStrongETag(response),
                StringComparison.Ordinal)
            || page.Items.Any(item => item.Sequence <= afterSequence)
            || page.Items.Select(item => item.Sequence).Distinct().Count()
               != page.Items.Count)
        {
            throw Protocol(
                "The conversation page identity, ETag, or sequence metadata was inconsistent.");
        }

        return page;
    }

    public async Task<ConversationCloudMutationResult> PutConversationAsync(
        string conversationId,
        AiConversationMutationRequest mutation,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        ValidateResourceId(conversationId, "conversation");
        ArgumentNullException.ThrowIfNull(mutation);
        using var request = CreateJsonPut(
            ConversationUri(conversationId),
            mutation,
            expectedETag);
        using var response = await SendAsync(request, cancellationToken);
        if (IsConflict(response.StatusCode))
        {
            return new ConversationCloudMutationResult(
                CloudMutationStatus.Conflict);
        }

        EnsureSuccess(response, $"upload conversation '{conversationId}'");
        var receipt = await ReadJsonAsync<AiConversationMutationReceipt>(
            response,
            64 * 1024,
            cancellationToken);
        ValidateProtocol(receipt.ProtocolVersion);
        ValidateConversationSummary(receipt.Conversation);
        ValidateReceiptHeaders(
            response,
            receipt.Conversation.ETag,
            receipt.Conversation.Revision);
        return new ConversationCloudMutationResult(
            response.StatusCode == HttpStatusCode.Created
                ? CloudMutationStatus.Created
                : CloudMutationStatus.Updated,
            receipt.Conversation);
    }

    public async Task<ConversationCloudMutationResult> DeleteConversationAsync(
        string conversationId,
        string expectedETag,
        CancellationToken cancellationToken = default)
    {
        ValidateResourceId(conversationId, "conversation");
        ValidateStrongETag(expectedETag);
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            ConversationUri(conversationId));
        request.Headers.TryAddWithoutValidation("If-Match", expectedETag);
        using var response = await SendAsync(request, cancellationToken);
        if (IsConflict(response.StatusCode))
        {
            return new ConversationCloudMutationResult(
                CloudMutationStatus.Conflict);
        }

        EnsureSuccess(response, $"delete conversation '{conversationId}'");
        var receipt = await ReadJsonAsync<AiConversationMutationReceipt>(
            response,
            64 * 1024,
            cancellationToken);
        ValidateConversationSummary(receipt.Conversation);
        if (!receipt.Conversation.IsDeleted)
        {
            throw Protocol("The conversation delete response was not a tombstone.");
        }

        return new ConversationCloudMutationResult(
            CloudMutationStatus.Deleted,
            receipt.Conversation);
    }

    public async Task<IReadOnlyList<HyperlibVaultPackageSummary>>
        ListPackagesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                CreateUri(HypershellCloudContract.RelativePackagesPath)),
            cancellationToken);
        EnsureSuccess(response, "list personal Hyperlib packages");
        var list = await ReadJsonAsync<HyperlibVaultPackageList>(
            response,
            HypershellCloudContract.MaximumPackageListBytes,
            cancellationToken);
        ValidateProtocol(list.ProtocolVersion);
        foreach (var package in list.Packages)
        {
            ValidatePackage(package);
        }

        return list.Packages;
    }

    public async Task<HyperlibVaultPackageSummary> UploadPackageAsync(
        string packageId,
        string version,
        string contentSha256,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        ValidateResourceId(packageId, "package");
        ValidateVersion(version);
        ValidateSha256(contentSha256);
        var file = new FileInfo(Path.GetFullPath(archivePath));
        if (!file.Exists
            || file.Length is <= 0
                or > HypershellCloudContract.MaximumPersonalPackageBytes)
        {
            throw Configuration(
                "Choose a non-empty .hyperlib archive within the 256 MiB limit.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            CreateUri(
                HypershellCloudContract.RelativePackagesPath
                + "/"
                + Uri.EscapeDataString(packageId)
                + "/"
                + Uri.EscapeDataString(version)
                + "/"
                + contentSha256));
        request.Content = new StreamContent(new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read));
        request.Content.Headers.ContentLength = file.Length;
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            HypershellCloudContract.HyperlibPackageMediaType);
        using var response = await SendAsync(request, cancellationToken);
        EnsureSuccess(response, $"upload personal package '{packageId}'");
        var receipt = await ReadJsonAsync<HyperlibVaultUploadReceipt>(
            response,
            64 * 1024,
            cancellationToken);
        ValidateProtocol(receipt.ProtocolVersion);
        ValidatePackage(receipt.Package);
        if (!string.Equals(
                receipt.Package.ContentSha256,
                contentSha256,
                StringComparison.Ordinal)
            || receipt.Package.IsOfficial)
        {
            throw Protocol("The personal-package upload receipt was inconsistent.");
        }

        return receipt.Package;
    }

    public async Task<string> DownloadPackageAsync(
        string contentSha256,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ValidateSha256(contentSha256);
        var destination = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(destination)
                        ?? throw Configuration(
                            "The package destination has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using var response = await SendAsync(
                new HttpRequestMessage(
                    HttpMethod.Get,
                    CreateUri(
                        HypershellCloudContract.RelativePackagesPath
                        + "/"
                        + contentSha256)),
                cancellationToken);
            EnsureSuccess(response, "download personal Hyperlib package");
            if (!string.Equals(
                    response.Content.Headers.ContentType?.MediaType,
                    HypershellCloudContract.HyperlibPackageMediaType,
                    StringComparison.OrdinalIgnoreCase)
                || response.Content.Headers.ContentLength
                   > HypershellCloudContract.MaximumPersonalPackageBytes)
            {
                throw Protocol("The package download metadata was invalid.");
            }

            await using (var input = await response.Content.ReadAsStreamAsync(
                             cancellationToken))
            await using (var output = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             128 * 1024,
                             FileOptions.Asynchronous
                             | FileOptions.WriteThrough))
            {
                var buffer = new byte[128 * 1024];
                long total = 0;
                while (true)
                {
                    var read = await input.ReadAsync(
                        buffer,
                        cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    if (read
                        > HypershellCloudContract.MaximumPersonalPackageBytes
                          - total)
                    {
                        throw Protocol(
                            "The package download exceeded 256 MiB.");
                    }

                    total += read;
                    await output.WriteAsync(
                        buffer.AsMemory(0, read),
                        cancellationToken);
                }

                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporary, destination, overwrite: true);
            return destination;
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

    private HttpRequestMessage CreateJsonPut<T>(
        Uri uri,
        T value,
        string? expectedETag)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        var request = new HttpRequestMessage(HttpMethod.Put, uri)
        {
            Content = new ByteArrayContent(bytes)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/json")
        {
            CharSet = "utf-8"
        };
        ApplyConditionalHeader(request, expectedETag);
        return request;
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
                throw Protocol(
                    "Cloud sync refused an HTTP redirect. Configure the final HTTPS endpoint directly.");
            }

            return response;
        }
        catch (OperationCanceledException exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Timeout,
                "The cloud-resource request timed out.",
                exception);
        }
        catch (OperationCanceledException exception)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Cancelled,
                "The cloud-resource request was cancelled.",
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

    private static async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var payload = await ReadBoundedAsync(
            response.Content,
            maximumBytes,
            cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<T>(payload.Span, JsonOptions)
                   ?? throw new JsonException("The response was empty.");
        }
        catch (JsonException exception)
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Protocol,
                $"The account service returned invalid JSON: {exception.Message}",
                exception);
        }
    }

    private static async Task<ReadOnlyMemory<byte>> ReadBoundedAsync(
        HttpContent content,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximumBytes)
        {
            throw Protocol("The account-service response exceeded its limit.");
        }

        await using var input = await content.ReadAsStreamAsync(
            cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (read > maximumBytes - output.Length)
            {
                throw Protocol(
                    "The account-service response exceeded its limit.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
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

        ValidateStrongETag(expectedETag);
        request.Headers.TryAddWithoutValidation("If-Match", expectedETag);
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
            HttpStatusCode.Unauthorized => ProfileSyncFailureKind.Authentication,
            HttpStatusCode.Forbidden => ProfileSyncFailureKind.Authorization,
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

    private static void ValidateWorkspaceSummary(
        WorkspaceProfileRoamingSummary profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateResourceId(profile.ProfileId, "workspace profile");
        ValidateStrongETag(profile.ETag);
        ValidateSha256(profile.ContentSha256);
        if (string.IsNullOrWhiteSpace(profile.DisplayName)
            || profile.ServerRevision < 1
            || profile.ClientRevision < 0
            || profile.UpdatedAtUtc == default)
        {
            throw Protocol("The server returned malformed workspace-profile metadata.");
        }
    }

    private static void ValidateConversationSummary(
        AiConversationSummary conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ValidateResourceId(conversation.Id, "conversation");
        ValidateResourceId(conversation.ProfileId, "workspace profile");
        ValidateStrongETag(conversation.ETag);
        if (string.IsNullOrWhiteSpace(conversation.Title)
            || conversation.Revision < 1
            || conversation.LastSequence < 0
            || conversation.CreatedAtUtc == default
            || conversation.UpdatedAtUtc == default)
        {
            throw Protocol("The server returned malformed conversation metadata.");
        }
    }

    private static void ValidatePackage(HyperlibVaultPackageSummary package)
    {
        ArgumentNullException.ThrowIfNull(package);
        ValidateResourceId(package.PackageId, "package");
        ValidateVersion(package.Version);
        ValidateSha256(package.ContentSha256);
        if (package.ByteLength is <= 0
            or > HypershellCloudContract.MaximumPersonalPackageBytes
            || package.CreatedAtUtc == default)
        {
            throw Protocol("The server returned malformed package metadata.");
        }
    }

    private static void ValidateReceiptHeaders(
        HttpResponseMessage response,
        string expectedETag,
        long expectedRevision)
    {
        if (!string.Equals(
                GetRequiredStrongETag(response),
                expectedETag,
                StringComparison.Ordinal)
            || !response.Headers.TryGetValues(
                ProfileSyncContract.ServerRevisionHeader,
                out var values)
            || !long.TryParse(
                values.SingleOrDefault(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var revision)
            || revision != expectedRevision)
        {
            throw Protocol("The mutation headers did not match its receipt.");
        }
    }

    private Uri WorkspaceProfileUri(string profileId) => CreateUri(
        HypershellCloudContract.RelativeWorkspaceProfilesPath
        + "/"
        + Uri.EscapeDataString(profileId));

    private Uri ConversationUri(string conversationId) => CreateUri(
        HypershellCloudContract.RelativeConversationsPath
        + "/"
        + Uri.EscapeDataString(conversationId));

    private Uri ConversationUri(
        string conversationId,
        long afterSequence,
        int pageSize) => CreateUri(
        HypershellCloudContract.RelativeConversationsPath
        + "/"
        + Uri.EscapeDataString(conversationId)
        + $"?afterSequence={afterSequence}&pageSize={pageSize}");

    private Uri CreateUri(string relativePath) => new(_endpoint, relativePath);

    private static string GetRequiredStrongETag(HttpResponseMessage response)
    {
        var value = response.Headers.ETag?.ToString() ?? string.Empty;
        ValidateStrongETag(value);
        return value;
    }

    private static void ValidateStrongETag(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.StartsWith("W/", StringComparison.OrdinalIgnoreCase)
            || value.Length is < 3 or > 258
            || value[0] != '"'
            || value[^1] != '"'
            || value.Any(char.IsControl))
        {
            throw Protocol("A strong quoted ETag is required.");
        }
    }

    private static void ValidateResourceId(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 128
            || !char.IsAsciiLetterOrDigit(value[0])
            || value.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '_' or ':' or '-')))
        {
            throw Configuration($"The {label} ID is malformed.");
        }
    }

    private static void ValidateVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 128
            || value.Any(char.IsWhiteSpace)
            || value.Count(character => character == '.') < 2)
        {
            throw Configuration("The package version is malformed.");
        }
    }

    private static void ValidateSha256(string value)
    {
        if (value?.Length != 64
            || !value.All(char.IsAsciiHexDigit)
            || !string.Equals(
                value,
                value.ToLowerInvariant(),
                StringComparison.Ordinal))
        {
            throw Configuration("The SHA-256 identity is malformed.");
        }
    }

    private static void ValidateProtocol(int protocolVersion)
    {
        if (protocolVersion != HypershellCloudContract.CurrentProtocolVersion)
        {
            throw Protocol(
                $"Cloud protocol version {protocolVersion} is unsupported.");
        }
    }

    private static bool IsConflict(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Conflict
            or HttpStatusCode.PreconditionFailed;

    private static void ValidateAccessToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)
            || accessToken.Length > 8192
            || accessToken.Any(character => character is '\r' or '\n'))
        {
            throw new ProfileSyncException(
                ProfileSyncFailureKind.Authentication,
                "A valid cloud session access token is required.");
        }
    }

    private static ProfileSyncException Configuration(string message) =>
        new(ProfileSyncFailureKind.Configuration, message);

    private static ProfileSyncException Protocol(string message) =>
        new(ProfileSyncFailureKind.Protocol, message);

    private static string GetApplicationVersion() =>
        typeof(HttpCloudRoamingTransport).Assembly.GetName().Version?.ToString()
        ?? "development";
}
