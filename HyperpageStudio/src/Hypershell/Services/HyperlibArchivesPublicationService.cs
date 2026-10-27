using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HyperLib.Core;

namespace Hypershell.Services;

/// <summary>The trusted desktop boundary owns the publishing credential. Web content can request only a reviewed local draft.</summary>
public sealed class HyperlibArchivesPublicationService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Hypershell.Hyperlib.Archives.v1");
    private readonly string _root;
    private readonly Func<bool> _sessionIsCurrent;
    internal Func<HttpMessageHandler>? TransportFactory { get; init; }
    public HyperlibArchivesPublicationService(string root, Func<bool> sessionIsCurrent)
    { _root = Path.GetFullPath(root); _sessionIsCurrent = sessionIsCurrent; }

    public object Status()
    {
        try
        {
            var configuration = ReadConfiguration();
            return new { canSync = configuration is not null, targetTitle = "Atlas Archives", message = configuration is null
                ? "Connect this Hyperlib installation to Atlas Archives to enable sync. Your selection is saved locally."
                : "Ready to sync to atlasuniversalis.com. Only checked items are updated." };
        }
        catch (Exception error) when (error is IOException or CryptographicException or JsonException or InvalidDataException)
        { return new { canSync = false, targetTitle = "Atlas Archives", message = "The Archives connection needs to be configured again. Your local draft is safe." }; }
    }

    public async Task<object> SyncAsync(JsonElement parameters, CancellationToken ct)
    {
        var configuration = ReadConfiguration() ?? throw new InvalidOperationException("Connect this installation to Atlas Archives before syncing.");
        RequireSession();
        var draftId = parameters.GetProperty("draftId").GetString() ?? "";
        var expectedDraftRevision = parameters.GetProperty("expectedDraftRevision").GetInt64();
        var expectedContentRevision = parameters.GetProperty("expectedContentRevision").GetInt64();
        var expectedReviewHash = parameters.GetProperty("reviewHash").GetString() ?? "";
        var publisher = new ArchivesPublicationService(_root);
        var draft = await publisher.GetDraftAsync(draftId, ct);
        using var client = new HttpClient(TransportFactory?.Invoke() ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { BaseAddress = new Uri(configuration.Origin + "/"), Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", configuration.Token);
        using var statusResponse = await client.GetAsync("Archives/api/status?publisherId=" + Uri.EscapeDataString(draft.PublisherId), ct);
        var status = await ReadResponseAsync(statusResponse, ct);
        if (!status.TryGetProperty("app", out var identity) || identity.GetString() != "hyperlib-archives"
            || !status.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1)
            throw new InvalidDataException("The Archives server does not support this publishing contract.");
        var expectedRelease = status.TryGetProperty("releaseId", out var release) && release.ValueKind == JsonValueKind.String ? release.GetString() : "";
        var package = await Task.Run(() => publisher.ExportAsync(draftId, expectedDraftRevision, expectedContentRevision, expectedReviewHash, ct), ct);
        RequireSession(); ct.ThrowIfCancellationRequested();
        if (new FileInfo(package.PackagePath).Length > 128L * 1024 * 1024)
            throw new InvalidDataException("This publication exceeds 128 MiB. Publish a smaller branch.");
        // Use the immutable package held open without write sharing until the receiver has accepted it.
        await using var stream = new FileStream(package.PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct)) != package.PackageSha256)
            throw new InvalidDataException("The reviewed publication package changed. Refresh the draft and retry.");
        stream.Position = 0;
        using var request = new HttpRequestMessage(HttpMethod.Post, "Archives/api/publish") { Content = new StreamContent(stream) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        request.Headers.Add("X-Archives-Manifest", package.ManifestSha256);
        request.Headers.Add("X-Archives-Expected-Release", expectedRelease ?? "");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var receipt = await ReadResponseAsync(response, ct);
        if (!receipt.TryGetProperty("manifestSha256", out var returnedHash) || returnedHash.GetString() != package.ManifestSha256
            || !receipt.TryGetProperty("publisherId", out var returnedPublisher) || returnedPublisher.GetString()?.Replace("-", "") != package.Manifest.PublisherId.Replace("-", ""))
            throw new InvalidDataException("The Archives receipt does not match this publication. Check Archives before retrying.");
        if (!receipt.TryGetProperty("url", out var url) || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var receiptUri)
            || receiptUri.GetLeftPart(UriPartial.Authority) != configuration.Origin || !receiptUri.AbsolutePath.StartsWith("/Archives/", StringComparison.Ordinal))
            throw new InvalidDataException("The Archives receipt contains an invalid public address.");
        var receiptDirectory = Path.Combine(Path.GetDirectoryName(ConfigurationPath(_root))!, "receipts");
        Directory.CreateDirectory(receiptDirectory);
        await File.WriteAllTextAsync(Path.Combine(receiptDirectory, package.ManifestSha256 + ".json"), receipt.GetRawText(), ct);
        return new { receipt, draft = await publisher.GetDraftAsync(draftId, ct) };
    }

    public static string ConfigurationPath(string root)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToUpperInvariant();
        var identity = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        var dataRoot = Environment.GetEnvironmentVariable(SettingsService.ApplicationDataOverrideVariable);
        dataRoot = string.IsNullOrWhiteSpace(dataRoot) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hypershell") : Path.GetFullPath(dataRoot);
        return Path.Combine(dataRoot, "Archives", identity, "connection.dpapi");
    }
    public static void Configure(string root, string origin, string token)
    {
        ValidateConfiguration(origin, token);
        var path = ConfigurationPath(root); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Connection(origin.TrimEnd('/'), token), ContentJson.Options);
        try
        {
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, protectedBytes); File.Move(temporary, path, true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private Connection? ReadConfiguration()
    {
        var path = ConfigurationPath(_root); if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 16384) throw new InvalidDataException("Invalid Archives connection.");
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        try
        {
            var configuration = JsonSerializer.Deserialize<Connection>(bytes, ContentJson.Options) ?? throw new InvalidDataException("Invalid Archives connection.");
            ValidateConfiguration(configuration.Origin, configuration.Token); return configuration;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static void ValidateConfiguration(string origin, string token)
    {
        if (origin.TrimEnd('/') != "https://atlasuniversalis.com" || token.Length is < 40 or > 512 || token.Any(char.IsWhiteSpace))
            throw new InvalidDataException("Use a scoped Atlas Archives connection for https://atlasuniversalis.com.");
    }
    private void RequireSession()
    {
        if (!_sessionIsCurrent()) throw new InvalidOperationException("This Hyperlib session is no longer attached. Reopen Hyperlib before syncing.");
    }
    private static async Task<JsonElement> ReadResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > 65536) throw new InvalidDataException("The Archives response is too large.");
            output.Write(buffer, 0, read);
        }
        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new InvalidOperationException("The Archives connection was not authorized. Reconnect this installation before syncing.");
            string? detail = null;
            try { using var error = JsonDocument.Parse(output.ToArray()); if (error.RootElement.TryGetProperty("error", out var value)) detail = value.GetString(); } catch (JsonException) { }
            throw new InvalidOperationException(detail is { Length: > 0 and <= 2000 } ? detail : $"Archives returned {(int)response.StatusCode}. Your local draft is retained.");
        }
        using var document = JsonDocument.Parse(output.ToArray()); return document.RootElement.Clone();
    }
    private sealed record Connection(string Origin, string Token);
}
