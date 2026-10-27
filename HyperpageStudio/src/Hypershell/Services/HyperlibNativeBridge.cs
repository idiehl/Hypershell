using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

/// <summary>A deliberately small bridge attached only to a native-launched HyperLib tab.</summary>
public sealed class HyperlibNativeBridge : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _root;
    private readonly Uri _origin;
    private readonly Func<bool> _isSessionRunning;
    private readonly Func<string, Task<bool>> _openStudio;
    private readonly HttpClient _client;
    private readonly HyperlibContentAiService _ai;
    private readonly HyperlibArchivesPublicationService _archives;
    private CancellationTokenSource? _activeRun;
    private readonly HashSet<string> _requestIds = new(StringComparer.Ordinal);
    private bool _disposed;
    public event Action<string>? EventJson;

    public HyperlibNativeBridge(string root, string address, Func<bool> isSessionRunning,
        Func<string, Task<bool>> openStudio, IWorkspaceAiProvider provider,
        WorkspaceAiProviderConfigurationService configuration, IWorkspaceAiCredentialStore credentials,
        WorkspaceAiContextService contexts, WorkspaceAiOperationService operations)
    {
        _root = Path.GetFullPath(root);
        _origin = new Uri(new Uri(address).GetLeftPart(UriPartial.Authority) + "/");
        if (_origin.Host != "127.0.0.1" || _origin.Scheme != "http")
            throw new InvalidOperationException("HyperLib requires its native-launched loopback origin.");
        _isSessionRunning = isSessionRunning;
        _openStudio = openStudio;
        _client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { BaseAddress = _origin, Timeout = TimeSpan.FromSeconds(20) };
        _client.DefaultRequestHeaders.Add("Origin", _origin.GetLeftPart(UriPartial.Authority));
        _client.DefaultRequestHeaders.Add("X-HyperLib-Client", "editor");
        _ai = new HyperlibContentAiService(provider, configuration, credentials, contexts, operations, ReadStateAsync, ApplyAsync, ReadTemplatesAsync, ReadBaseTemplatesAsync);
        _archives = new HyperlibArchivesPublicationService(_root, () => AcceptsOrigin(_origin.AbsoluteUri));
    }

    public bool AcceptsOrigin(string? address) => !_disposed && _isSessionRunning()
        && Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.Scheme == _origin.Scheme && uri.Host == _origin.Host && uri.Port == _origin.Port;

    public void NavigationChanged()
    {
        _activeRun?.Cancel();
        _requestIds.Clear();
    }

    public async Task<string?> HandleAsync(string source, string topLevelSource, string message)
    {
        if (!AcceptsOrigin(source) || !AcceptsOrigin(topLevelSource)
            || new Uri(source).GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped)
                != new Uri(topLevelSource).GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped)) return null;
        string? id = null;
        var publishing = false;
        try
        {
            if (Encoding.UTF8.GetByteCount(message) > 128 * 1024) throw new InvalidDataException("The bridge request is too large.");
            using var document = JsonDocument.Parse(message, new JsonDocumentOptions { MaxDepth = 32 });
            var request = document.RootElement;
            if (request.GetProperty("channel").GetString() != "hyperlib") return null;
            id = request.GetProperty("id").GetString();
            if (id is null || !Regex.IsMatch(id, "^[A-Za-z0-9._:-]{1,80}$")) throw new InvalidDataException("Invalid request ID.");
            if (_requestIds.Count >= 4096) throw new InvalidOperationException("Reload HyperLib to start a new bridge session.");
            if (!_requestIds.Add(id)) throw new InvalidDataException("A request ID cannot be reused.");
            var method = request.GetProperty("method").GetString();
            var parameters = request.TryGetProperty("params", out var p) ? p : JsonSerializer.SerializeToElement(new { });
            if (method == "ai.cancel")
            {
                _activeRun?.Cancel();
                return Reply(id, new { cancelled = true });
            }
            var state = await ReadStateAsync(CancellationToken.None);
            var libraryId = state.GetProperty("libraryId").GetString();
            if (method == "capabilities") return Reply(id, _ai.Capabilities(libraryId!));
            if (!parameters.TryGetProperty("libraryId", out var suppliedId) || suppliedId.GetString() != libraryId)
                throw new UnauthorizedAccessException("The library identity does not match this native tab.");
            if (method == "archives.status") return Reply(id, _archives.Status());
            if (method == "archives.sync")
            {
                publishing = true;
                if (_activeRun is not null) throw new InvalidOperationException("Wait for the current operation to finish.");
                using var publication = new CancellationTokenSource();
                _activeRun = publication;
                try { return Reply(id, await _archives.SyncAsync(parameters, publication.Token)); }
                finally { _activeRun = null; }
            }
            if (method == "openStudio")
            {
                var path = _root;
                if (parameters.TryGetProperty("workId", out var workId) && !string.IsNullOrWhiteSpace(workId.GetString()))
                {
                    var work = state.GetProperty("nodes").EnumerateArray().FirstOrDefault(node =>
                        node.GetProperty("id").GetString() == workId.GetString() && node.GetProperty("type").GetString() == "work");
                    if (work.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("That work is not in this library.");
                    path = HyperlibMyNetRegistration.ContainedProject(_root, work.GetProperty("projectPath").GetString()!);
                }
                if (!File.Exists(Path.Combine(path, "visual-page.project.json")))
                    throw new FileNotFoundException("This project's Studio manifest is not available.");
                return Reply(id, new { opened = await _openStudio(path) });
            }
            if (method != "ai.send") throw new InvalidDataException("This bridge method is not supported.");
            if (!parameters.TryGetProperty("editMode", out var edit) || edit.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("Enter Edit mode to use the content assistant.");
            if (_activeRun is not null) throw new InvalidOperationException("Wait for the active assistant run or cancel it.");
            var prompt = parameters.GetProperty("prompt").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 24000) throw new InvalidDataException("Enter a prompt of at most 24,000 characters.");
            using var run = new CancellationTokenSource();
            _activeRun = run;
            try
            {
                var result = await _ai.SendAsync(prompt, state, parameters.TryGetProperty("context", out var context) ? context : default,
                    text => EventJson?.Invoke(JsonSerializer.Serialize(new { channel = "hyperlib", @event = "ai.progress", requestId = id, message = text }, JsonOptions)), run.Token);
                return Reply(id, result);
            }
            finally { _activeRun = null; }
        }
        catch (OperationCanceledException) { return Error(id, publishing
            ? "The sync was interrupted. Your draft is retained; retry the same draft to check whether Archives received it."
            : "The assistant run was cancelled. Completed content changes remain available to Undo."); }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or InvalidOperationException
            or UnauthorizedAccessException or IOException or HttpRequestException or KeyNotFoundException or WorkspaceAiProviderException
            or HyperLib.Core.ContentValidationException or FormatException or OverflowException)
        { return Error(id, exception.Message); }
    }

    private async Task<JsonElement> ReadStateAsync(CancellationToken cancellationToken)
    {
        if (!AcceptsOrigin(_origin.AbsoluteUri)) throw new UnauthorizedAccessException("This HyperLib process is no longer attached.");
        using var response = await _client.GetAsync("api/state", cancellationToken);
        return await ReadResponseAsync(response, cancellationToken);
    }

    private async Task<JsonElement> ApplyAsync(JsonElement batch, CancellationToken cancellationToken)
    {
        if (!AcceptsOrigin(_origin.AbsoluteUri)) throw new UnauthorizedAccessException("This HyperLib process is no longer attached.");
        using var content = new StringContent(batch.GetRawText(), Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync("api/operations", content, cancellationToken);
        return await ReadResponseAsync(response, cancellationToken);
    }

    private async Task<JsonElement> ReadTemplatesAsync(string workId, CancellationToken cancellationToken)
    {
        var state = await ReadStateAsync(cancellationToken);
        if (workId != "library" && !state.GetProperty("nodes").EnumerateArray().Any(node =>
                node.GetProperty("id").GetString() == workId && node.GetProperty("type").GetString() == "work"))
            throw new InvalidDataException("That work is not in this library.");
        using var response = await _client.GetAsync("api/templates/" + Uri.EscapeDataString(workId), cancellationToken);
        return await ReadResponseAsync(response, cancellationToken);
    }

    private async Task<JsonElement> ReadBaseTemplatesAsync(CancellationToken cancellationToken)
    {
        if (!AcceptsOrigin(_origin.AbsoluteUri)) throw new UnauthorizedAccessException("This HyperLib process is no longer attached.");
        using var response = await _client.GetAsync("api/base-templates", cancellationToken);
        return await ReadResponseAsync(response, cancellationToken);
    }

    private static async Task<JsonElement> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (text.Length > 4 * 1024 * 1024) throw new InvalidDataException("The library response is too large for this assistant turn.");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"HyperLib returned {(int)response.StatusCode}: {text[..Math.Min(text.Length, 600)]}");
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static string Reply(string? id, object result) => JsonSerializer.Serialize(new { channel = "hyperlib", id, result, error = (string?)null }, JsonOptions);
    private static string Error(string? id, string error) => JsonSerializer.Serialize(new { channel = "hyperlib", id, result = (object?)null, error }, JsonOptions);

    public void Dispose()
    {
        _disposed = true;
        _activeRun?.Cancel();
        _client.Dispose();
    }
}
