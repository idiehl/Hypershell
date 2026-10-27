using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using HyperLib.Core;
using Hypershell.Services;
using Hypershell.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Hypershell.Controls;

public sealed record StudioWorkspaceChromeTab(string Id, string Title, bool CanClose, string HelpTitle);
public sealed record StudioWorkspacePanelOption(string Id, string Title);
public sealed record StudioWorkspacePanelSnapshot(string SelectedId, bool IsOpen, IReadOnlyList<StudioWorkspacePanelOption> Options);
public sealed record StudioWorkspaceChromeSnapshot(IReadOnlyList<StudioWorkspaceChromeTab> Tabs, string ActiveId, bool CanSave,
    StudioWorkspacePanelSnapshot? LeftPanel = null, StudioWorkspacePanelSnapshot? RightPanel = null);
public sealed record StudioWorkspaceAnchor(double Left, double Top, double Width, double Height, double ViewportWidth, double ViewportHeight);
public sealed record StudioWorkspaceAction(string Action, string? ContentId, StudioWorkspaceAnchor? Anchor, string? Side = null);

/// <summary>The real Hyperlib content writer, hosted inside a Studio project rather than in the reader.</summary>
public partial class StudioHyperlibAuthoringView : UserControl, IDisposable
{
    private const int MaxRecoveryBytes = 8 * 1024 * 1024;
    private readonly HyperlibStudioWorkspaceViewModel _model;
    private readonly string? _webViewProfileDirectory;
    private readonly StudioAuthoringLibraryService _libraryService;
    private readonly SemaphoreSlim _libraryGate = new(1, 1);
    private readonly HashSet<string> _libraryRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _libraryReplies = new(StringComparer.Ordinal);
    private CancellationTokenSource? _libraryPreviewCancellation;
    private int _libraryMutationsInFlight;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, TaskCompletionSource<bool>> _flushes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<bool>> _catalogCommits = new(StringComparer.Ordinal);
    private long _catalogPreviewVersion;
    private readonly HyperlibStudioWorkspaceService _gateway = new();
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private WebView2? _browser;
    private Uri? _endpoint;
    private string? _recoveryPath;
    private string? _pendingNode;
    private string? _recoveryScript;
    private long? _lastRevision;
    private bool _ready;
    private bool _navigating;
    private bool _disposed;
    private string? _lastRecovery;
    private TaskCompletionSource<bool>? _readyCompletion;
    private StudioWorkspaceChromeSnapshot? _workspaceChrome;
    private string? _workspaceChromeJson;
    public bool IsWorkspaceChromeReady { get; private set; }
    public bool HasUnsavedContent { get; private set; }
    public string? CurrentNodeId { get; private set; }
    public string? CurrentWorkId { get; private set; }
    public event Action<bool>? DraftStateChanged;
    public event Action? ContentChanged;
    public event Action<string>? StatusChanged;
    public event Action? NewWorkRequested;
    public event Action? PageTemplatesRequested;
    public event Action? PublicationRequested;
    public event Action<StudioWorkspaceAction>? WorkspaceActionRequested;
    public event Action<bool>? WorkspaceChromeReadyChanged;

    public StudioHyperlibAuthoringView(HyperlibStudioWorkspaceViewModel model, string? webViewProfileDirectory = null,
        StudioAuthoringLibraryService? libraryService = null)
    {
        InitializeComponent();
        _model = model;
        _webViewProfileDirectory = webViewProfileDirectory;
        _libraryService = libraryService ?? new StudioAuthoringLibraryService();
    }

    private async void ViewLoaded(object sender, RoutedEventArgs e) => await InitializeAsync();
    private async void RetryClick(object sender, RoutedEventArgs e) => await InitializeAsync();

    /// <summary>Mirrors the native pane's tabs; the native pane remains the owner of selection and file buffers.</summary>
    public void SetWorkspaceChrome(StudioWorkspaceChromeSnapshot chrome)
    {
        if (_disposed) return;
        var json = JsonSerializer.Serialize(chrome, ContentJson.Options);
        if (_workspaceChromeJson == json) return;
        if (_workspaceChrome?.ActiveId != chrome.ActiveId) SetWorkspaceChromeReady(false);
        _workspaceChrome = chrome;
        _workspaceChromeJson = json;
        _ = SendWorkspaceChromeAsync();
    }

    private async Task SendWorkspaceChromeAsync()
    {
        if (_disposed || !_ready || _browser?.CoreWebView2 is null || _workspaceChromeJson is null) return;
        try
        {
            await _browser.CoreWebView2.ExecuteScriptAsync("window.__studioAuthoring?.configureWorkspaceChrome?.(" + _workspaceChromeJson + ");");
        }
        catch (Exception error) when (error is InvalidOperationException or OperationCanceledException)
        {
            SetWorkspaceChromeReady(false);
            if (!_disposed) StatusChanged?.Invoke("The workspace tabs are using their native controls. " + error.Message);
        }
    }

    private void SetWorkspaceChromeReady(bool ready)
    {
        if (IsWorkspaceChromeReady == ready) return;
        IsWorkspaceChromeReady = ready;
        WorkspaceChromeReadyChanged?.Invoke(ready);
    }

    private void ReceiveWorkspaceAction(JsonElement data)
    {
        if (!IsWorkspaceChromeReady || _workspaceChrome is null) return;
        var request = JsonSerializer.Deserialize<StudioWorkspaceAction>(data.GetRawText(), ContentJson.Options);
        if (request is null || request.Action is not ("select" or "close" or "help" or "new" or "layouts" or "authoring" or "publish" or "save" or "panel-select" or "panel-toggle"))
            throw new InvalidDataException("The workspace action is not supported.");
        if (request.Action is "panel-select" or "panel-toggle")
        {
            var panel = request.Side switch { "left" => _workspaceChrome.LeftPanel, "right" => _workspaceChrome.RightPanel, _ => null };
            if (panel is null || (request.Action == "panel-select" && !panel.Options.Any(option => option.Id == request.ContentId)))
                throw new InvalidDataException("This workspace panel is no longer available.");
        }
        if (request.Action is "select" or "close" or "help")
        {
            var tab = _workspaceChrome.Tabs.FirstOrDefault(tab => tab.Id == request.ContentId)
                ?? throw new InvalidDataException("This workspace tab is no longer open.");
            if (request.Action == "close" && !tab.CanClose) return;
        }
        if (request.Action == "new")
        {
            var anchor = request.Anchor ?? throw new InvalidDataException("The document menu needs its button position.");
            var values = new[] { anchor.Left, anchor.Top, anchor.Width, anchor.Height, anchor.ViewportWidth, anchor.ViewportHeight };
            if (values.Any(value => !double.IsFinite(value)) || anchor.Left < 0 || anchor.Top < 0
                || anchor.Width < 0 || anchor.Height < 0 || anchor.ViewportWidth is <= 0 or > 100000
                || anchor.ViewportHeight is <= 0 or > 100000 || anchor.Left + anchor.Width > anchor.ViewportWidth + 1
                || anchor.Top + anchor.Height > anchor.ViewportHeight + 1)
                throw new InvalidDataException("The document menu position is outside this workspace.");
        }
        WorkspaceActionRequested?.Invoke(request);
    }

    private async Task InitializeAsync()
    {
        if (_disposed || _ready || _navigating) return;
        await _initialization.WaitAsync();
        try
        {
            if (_disposed || _ready || _navigating) return;
            LoadingPanel.Visibility = Visibility.Visible;
            RetryButton.Visibility = Visibility.Collapsed;
            LoadingText.Text = "Opening this project’s content tools…";
            if (_model.Context is null) await _model.LoadAsync(_lifetime.Token);
            var context = _model.Context ?? throw new InvalidOperationException("The project’s Hyperlib content could not be read.");
            if (!context.HasLibraryContent) throw new InvalidOperationException(context.ContentUnavailableReason);
            if (HyperlibWorkPreviewHostService.IsGeneratedLibrary(context.LibraryRoot!))
            {
                LoadingText.Text = "Preparing the project’s page editor…";
                try
                {
                    if (await new HyperlibWorkProjectService(context.LibraryRoot).EnsureDirectEditorSupportAsync(context.ProjectRoot, _lifetime.Token))
                        await _model.RefreshAsync();
                }
                catch (InvalidDataException error) when (error.Message.Contains("custom", StringComparison.OrdinalIgnoreCase))
                {
                    // A customized renderer keeps its existing editing capabilities and source.
                    StatusChanged?.Invoke("The project's custom renderer was preserved. " + error.Message);
                }
            }
            _endpoint = await _gateway.ConnectAsync(context,
                _model.EndpointFactory ?? throw new InvalidOperationException("Connect the project’s local preview host first."), _lifetime.Token);
            if (_disposed) return;
            _recoveryPath = VisualPageProjectService.ResolveProjectPath(context.ProjectRoot, ".studio/authoring-draft.json");
            var recovery = ReadRecovery();
            if (_browser is null)
            {
                _browser = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 15, 21, 27) };
                BrowserHost.Children.Add(_browser);
                var profileIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context.ProjectRoot.ToUpperInvariant())))[..20];
                var profile = _webViewProfileDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Hypershell", "StudioAuthoringWebView", profileIdentity);
                var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
                await _browser.EnsureCoreWebView2Async(environment);
                if (_disposed) return;
                var core = _browser.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsWebMessageEnabled = true;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.AddWebResourceRequestedFilter("*/studio-authoring/*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += ServeAuthoringAsset;
                core.WebMessageReceived += ReceiveMessage;
                core.NavigationStarting += GuardNavigation;
                core.NavigationCompleted += NavigationCompleted;
                core.NewWindowRequested += OpenExternalLink;
            }
            if (_recoveryScript is not null) _browser.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_recoveryScript);
            _recoveryScript = await _browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                "if(window===window.top){window.__studioAuthoringRecovery=" + (recovery ?? "null") + ";}");
            var pipeline = await new HyperlibProjectPipelineService().ReadAsync(context.ProjectRoot, _lifetime.Token);
            var node = _pendingNode ?? pipeline?.LandingPageId ?? context.WorkId;
            _readyCompletion?.TrySetResult(false);
            _readyCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _navigating = true;
            SetWorkspaceChromeReady(false);
            _browser.CoreWebView2.Navigate(new Uri(_endpoint, "studio-authoring/index.html" +
                (node is null ? "" : "#" + Uri.EscapeDataString(node))).AbsoluteUri);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (_disposed) return;
            _navigating = false;
            _readyCompletion?.TrySetResult(false);
            LoadingText.Text = error.Message;
            RetryButton.Visibility = Visibility.Visible;
            StatusChanged?.Invoke(error.Message);
        }
        finally { _initialization.Release(); }
    }

    private void ServeAuthoringAsset(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (_endpoint is null || !Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)
            || uri.GetLeftPart(UriPartial.Authority) != _endpoint.GetLeftPart(UriPartial.Authority)) return;
        var fileName = uri.AbsolutePath switch
        {
            "/studio-authoring/index.html" => "index.html",
            "/studio-authoring/app.js" => "app.js",
            "/studio-authoring/element-editor.js" => "element-editor.js",
            "/studio-authoring/app.css" => "app.css",
            "/studio-authoring/studio.css" => "studio.css",
            _ => null
        };
        if (fileName is null) return;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "StudioHyperlibAuthoring", fileName);
            var bytes = File.ReadAllBytes(path);
            var mime = fileName.EndsWith(".html", StringComparison.Ordinal) ? "text/html" :
                fileName.EndsWith(".js", StringComparison.Ordinal) ? "text/javascript" : "text/css";
            e.Response = _browser!.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(bytes, writable: false),
                200, "OK", "Content-Type: " + mime + "; charset=utf-8\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff");
        }
        catch (IOException error) { StatusChanged?.Invoke("The Studio content tools are missing: " + error.Message); }
    }

    private void GuardNavigation(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_endpoint is null || !Uri.TryCreate(e.Uri, UriKind.Absolute, out var target)
            || target.GetLeftPart(UriPartial.Authority) != _endpoint.GetLeftPart(UriPartial.Authority)
            || target.AbsolutePath != "/studio-authoring/index.html") e.Cancel = true;
    }

    private void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess) return;
        _ready = false;
        SetWorkspaceChromeReady(false);
        _navigating = false;
        _readyCompletion?.TrySetResult(false);
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingText.Text = "The project content tools could not open. " + e.WebErrorStatus;
        RetryButton.Visibility = Visibility.Visible;
    }

    private void ReceiveMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_disposed || _endpoint is null || !Uri.TryCreate(e.Source, UriKind.Absolute, out var source)
            || source.GetLeftPart(UriPartial.Authority) != _endpoint.GetLeftPart(UriPartial.Authority)
            || source.AbsolutePath != "/studio-authoring/index.html") return;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var data = document.RootElement;
            if (!data.TryGetProperty("type", out var type)) return;
            switch (type.GetString())
            {
                case "studio:catalog-commit-result":
                    if (data.TryGetProperty("requestId", out var catalogRequest)
                        && catalogRequest.ValueKind == JsonValueKind.String
                        && _catalogCommits.TryGetValue(catalogRequest.GetString()!, out var catalogCommit))
                        catalogCommit.TrySetResult(data.TryGetProperty("success", out var catalogSuccess)
                            && catalogSuccess.ValueKind == JsonValueKind.True);
                    break;
                case "studio:library-request":
                    _ = HandleLibraryRequestAsync(data.Clone());
                    break;
                case "studio:library-preview-cancel":
                    _libraryPreviewCancellation?.Cancel();
                    break;
                case "studio:workspace-chrome-ready":
                    if (_ready && _workspaceChrome?.ActiveId == "canvas"
                        && data.TryGetProperty("activeId", out var chromeActive) && chromeActive.GetString() == "canvas")
                        SetWorkspaceChromeReady(true);
                    break;
                case "studio:workspace-action":
                    ReceiveWorkspaceAction(data);
                    break;
                case "studio:state":
                    var wasReady = _ready;
                    if (data.TryGetProperty("nodeId", out var currentNode)) CurrentNodeId = currentNode.GetString();
                    if (data.TryGetProperty("workId", out var currentWork)) CurrentWorkId = currentWork.GetString();
                    if (CurrentWorkId is { } workId && _model.Works.FirstOrDefault(work => work.Id == workId) is { } selectedWork)
                        _model.SelectedWork = selectedWork;
                    if (CurrentNodeId is { } pageId && _model.Pages.FirstOrDefault(page => page.Id == pageId) is { } selectedPage)
                        _model.SelectedPage = selectedPage;
                    _ready = data.TryGetProperty("ready", out var ready) && ready.ValueKind == JsonValueKind.True;
                    if (_ready) { _navigating = false; LoadingPanel.Visibility = Visibility.Collapsed; _readyCompletion?.TrySetResult(true); }
                    if (_ready && !wasReady) _ = SendWorkspaceChromeAsync();
                    var dirty = data.TryGetProperty("dirty", out var pending) && pending.ValueKind == JsonValueKind.True;
                    if (_ready || dirty) SetDirty(dirty);
                    if (data.TryGetProperty("status", out var status)) StatusChanged?.Invoke(status.GetString() ?? "Saved locally");
                    if (data.TryGetProperty("revision", out var revision) && revision.TryGetInt64(out var currentRevision))
                    {
                        if (_lastRevision.HasValue && currentRevision > _lastRevision) ContentChanged?.Invoke();
                        _lastRevision = currentRevision;
                    }
                    // The initial render precedes restoration. It must never erase
                    // an on-disk draft before the frontend has finished loading it.
                    if (_ready || data.TryGetProperty("draft", out var startupDraft) && startupDraft.ValueKind == JsonValueKind.Object)
                        PersistRecovery(data);
                    break;
                case "studio:flush-result":
                case "studio:refresh-result":
                    if (data.TryGetProperty("requestId", out var id) && id.GetString() is { } key
                        && _flushes.Remove(key, out var completion))
                    {
                        var saved = data.TryGetProperty(type.GetString() == "studio:refresh-result" ? "success" : "saved", out var result)
                            && result.ValueKind == JsonValueKind.True;
                        if (!saved && data.TryGetProperty("error", out var failure) && failure.ValueKind == JsonValueKind.String)
                            StatusChanged?.Invoke(failure.GetString()!);
                        completion.TrySetResult(saved);
                    }
                    break;
                case "studio:new-work": NewWorkRequested?.Invoke(); break;
                case "studio:page-templates": PageTemplatesRequested?.Invoke(); break;
                case "studio:publish": PublicationRequested?.Invoke(); break;
                case "studio:load-error":
                    _ready = false;
                    SetWorkspaceChromeReady(false);
                    _navigating = false;
                    _readyCompletion?.TrySetResult(false);
                    LoadingPanel.Visibility = Visibility.Visible;
                    LoadingText.Text = data.TryGetProperty("message", out var loadFailure) ? loadFailure.GetString() : "The content tools could not open.";
                    RetryButton.Visibility = Visibility.Visible;
                    break;
                case "studio:error":
                    if (data.TryGetProperty("message", out var message)) StatusChanged?.Invoke(message.GetString() ?? "The content tools need attention.");
                    break;
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException)
        { StatusChanged?.Invoke(error.Message); }
    }

    private async Task HandleLibraryRequestAsync(JsonElement request)
    {
        static string Required(JsonElement data, string name)
            => data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!
                : throw new InvalidDataException("The library request is missing " + name + ".");
        static string? Optional(JsonElement data, string name)
            => data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        string? requestId = null;
        var entered = false;
        var mutationEntered = false;
        CancellationTokenSource? previewCancellation = null;
        try
        {
            requestId = Required(request, "requestId");
            if (requestId.Length > 128) throw new InvalidDataException("The library request identity is too long.");
            if (_libraryReplies.TryGetValue(requestId, out var completed))
            {
                if (!_disposed) _browser?.CoreWebView2?.PostWebMessageAsJson(completed);
                return;
            }
            if (!_libraryRequests.Add(requestId)) return;
            entered = true;
            var action = Required(request, "action");
            if (action == "preview")
            {
                _libraryPreviewCancellation?.Cancel();
                previewCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                _libraryPreviewCancellation = previewCancellation;
            }
            if (action is "add" or "create-landing" or "save-page-template")
            {
                _libraryPreviewCancellation?.Cancel();
                _libraryMutationsInFlight++;
                mutationEntered = true;
            }
            var requestCancellation = previewCancellation?.Token ?? _lifetime.Token;
            await _libraryGate.WaitAsync(requestCancellation);
            try
            {
                var context = _model.Context ?? throw new InvalidOperationException("Open a Work project to use its library.");
                object result;
                if (action == "catalog") result = await _libraryService.ListAsync(context.ProjectRoot, _lifetime.Token);
                else if (action == "preview")
                    result = await _libraryService.PreviewAsync(context.ProjectRoot, Required(request, "itemId"), requestCancellation);
                else
                {
                    if (!request.TryGetProperty("expectedContentRevision", out var revision) || !revision.TryGetInt64(out var expected))
                        throw new InvalidDataException("Save the Page before changing its template library.");
                    if (action is "add" or "create-landing")
                    {
                        double? insertionY = null;
                        if (request.TryGetProperty("insertionY", out var insertion) && insertion.ValueKind != JsonValueKind.Null)
                        {
                            if (!insertion.TryGetDouble(out var y) || !double.IsFinite(y) || y is < 0 or > 100000)
                                throw new InvalidDataException("The insertion position is outside this Page.");
                            insertionY = y;
                        }
                        var added = action == "create-landing"
                            ? await _libraryService.CreateLandingAsync(context.ProjectRoot, Optional(request, "title"), expected,
                                Optional(request, "templateId"), _lifetime.Token)
                            : await _libraryService.AddAsync(context.ProjectRoot, Required(request, "itemId"),
                                Required(request, "pageId"), Optional(request, "regionId"), expected,
                                Optional(request, "title"), Optional(request, "parentId"), _lifetime.Token, insertionY: insertionY,
                                targetRole: Optional(request, "targetRole"));
                        // Imported component source and schemas must be visible to the running gateway
                        // before the browser refreshes its palette and selected Page.
                        result = added;
                        try
                        {
                            await _model.RefreshAsync();
                            await _gateway.PreviewAsync(_model.Context ?? context, added.State,
                                _endpoint ?? throw new InvalidOperationException("The project gateway is unavailable."), added.PageId, _lifetime.Token);
                        }
                        catch (Exception error) when (error is not OperationCanceledException)
                        {
                            // The revision is already committed. Report success with a renderer
                            // warning so retrying a failed build cannot insert the same item twice.
                            result = new { added.State, added.PageId, added.AddedBlockIds,
                                warning = "The Page was saved. Its renderer needs attention: " + error.Message };
                        }
                        ContentChanged?.Invoke();
                    }
                    else if (action == "save-page-template")
                    {
                        result = await _libraryService.SavePageTemplateAsync(context.ProjectRoot, Required(request, "pageId"),
                            Required(request, "title"), expected, _lifetime.Token);
                        ContentChanged?.Invoke();
                    }
                    else throw new InvalidDataException("This library action is not available.");
                }
                requestCancellation.ThrowIfCancellationRequested();
                ReplyLibraryRequest(requestId, true, result, null);
            }
            finally { _libraryGate.Release(); }
        }
        catch (Exception) when (previewCancellation?.IsCancellationRequested == true && !_lifetime.IsCancellationRequested)
        {
            if (requestId is not null) ReplyLibraryRequest(requestId, false, null, "Preview selection changed.");
        }
        catch (Exception error)
        {
            if (!_disposed && error is not OperationCanceledException)
            {
                StatusChanged?.Invoke(error.Message);
                if (requestId is not null) ReplyLibraryRequest(requestId, false, null, error.Message);
            }
        }
        finally
        {
            if (mutationEntered) _libraryMutationsInFlight--;
            if (entered && requestId is not null) _libraryRequests.Remove(requestId);
            if (ReferenceEquals(_libraryPreviewCancellation, previewCancellation)) _libraryPreviewCancellation = null;
            previewCancellation?.Dispose();
        }
    }

    private void ReplyLibraryRequest(string requestId, bool success, object? result, string? error)
    {
        if (_disposed || _browser?.CoreWebView2 is null) return;
        var json = JsonSerializer.Serialize(new { type = "studio:library-result", requestId, success, result, error }, ContentJson.Options);
        _libraryReplies[requestId] = json;
        while (_libraryReplies.Count > 1 && (_libraryReplies.Count > 8 || _libraryReplies.Values.Sum(value => (long)value.Length) > 4 * 1024 * 1024))
            _libraryReplies.Remove(_libraryReplies.Keys.First());
        _browser.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void SetDirty(bool dirty)
    {
        if (HasUnsavedContent == dirty) return;
        HasUnsavedContent = dirty;
        DraftStateChanged?.Invoke(dirty);
    }

    private string? ReadRecovery()
    {
        if (_recoveryPath is null || !File.Exists(_recoveryPath)) return null;
        VisualPageProjectService.RejectReparsePoint(_recoveryPath);
        if (new FileInfo(_recoveryPath).Length > MaxRecoveryBytes) throw new InvalidDataException("The recovered content draft is too large. Keep the recovery file before resetting it.");
        var json = File.ReadAllText(_recoveryPath);
        using var parsed = JsonDocument.Parse(json);
        if (parsed.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The recovered content draft is not valid.");
        _lastRecovery = json;
        SetDirty(true);
        return json;
    }

    private void PersistRecovery(JsonElement data)
    {
        if (_recoveryPath is null) return;
        if (!data.TryGetProperty("draft", out var draft) || draft.ValueKind == JsonValueKind.Null)
        {
            if (HasUnsavedContent) return;
            if (File.Exists(_recoveryPath)) { VisualPageProjectService.RejectReparsePoint(_recoveryPath); File.Delete(_recoveryPath); }
            _lastRecovery = null;
            return;
        }
        if (draft.ValueKind != JsonValueKind.Object || !data.TryGetProperty("libraryId", out var library)
            || library.GetString() != _model.Context?.LibraryId) throw new InvalidDataException("This draft does not belong to the open project.");
        var json = JsonSerializer.Serialize(new { libraryId = library.GetString(), draft,
            invalidFields = data.TryGetProperty("invalidFields", out var fields) ? fields : JsonSerializer.SerializeToElement(Array.Empty<string>()) });
        if (json == _lastRecovery) return;
        if (Encoding.UTF8.GetByteCount(json) > MaxRecoveryBytes) throw new InvalidDataException("The Page draft is too large for automatic recovery. Save or download it before closing.");
        var directory = Path.GetDirectoryName(_recoveryPath)!;
        Directory.CreateDirectory(directory);
        VisualPageProjectService.RejectReparsePoint(directory);
        if (File.Exists(_recoveryPath)) VisualPageProjectService.RejectReparsePoint(_recoveryPath);
        var pending = Path.Combine(directory, ".authoring-draft-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllText(pending, json, new UTF8Encoding(false)); File.Move(pending, _recoveryPath, overwrite: true); _lastRecovery = json; }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    public async Task<bool> SaveAsync()
    {
        if (_disposed) return !HasUnsavedContent;
        if (!_ready || _browser?.CoreWebView2 is null) return !HasUnsavedContent;
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _flushes[requestId] = completion;
        try
        {
            await _browser.CoreWebView2.ExecuteScriptAsync("window.__studioAuthoring?.flush(" + JsonSerializer.Serialize(requestId) + ");");
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(_libraryMutationsInFlight > 0 ? 180 : 15), _lifetime.Token);
        }
        catch (Exception error) when (error is TimeoutException or OperationCanceledException or InvalidOperationException)
        { if (!_disposed) StatusChanged?.Invoke("The Page could not confirm its save: " + error.Message); return false; }
        finally { _flushes.Remove(requestId); }
    }

    public async Task<bool> PrepareToLeaveAsync()
    {
        if (await SaveAsync()) return true;
        if (!HasUnsavedContent) return true;
        var owner = Window.GetWindow(this);
        if (_lastRecovery is null)
        {
            const string unfinished = "The page has an inline edit that has not confirmed its save yet. Keep this project open until the content tools finish saving, then try again.";
            if (owner is null) MessageBox.Show(unfinished, "Content still saving", MessageBoxButton.OK, MessageBoxImage.Information);
            else MessageBox.Show(owner, unfinished, "Content still saving", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var message = "The current Page has changes that could not be saved. Its recoverable draft stays in this project.\n\nChoose Yes to leave and recover it later, or No to continue editing.";
        return (owner is null ? MessageBox.Show(message, "Keep content draft", MessageBoxButton.YesNo, MessageBoxImage.Information)
            : MessageBox.Show(owner, message, "Keep content draft", MessageBoxButton.YesNo, MessageBoxImage.Information)) == MessageBoxResult.Yes;
    }

    public async Task NavigateToAsync(string nodeId)
    {
        _pendingNode = nodeId;
        if (!await EnsureReadyAsync() || _browser?.CoreWebView2 is null) return;
        if (CurrentNodeId == nodeId) return;
        if (!await SaveAsync()) return;
        await _browser.CoreWebView2.ExecuteScriptAsync("window.__studioAuthoring?.navigate(" + JsonSerializer.Serialize(nodeId)
            + "," + JsonSerializer.Serialize(Guid.NewGuid().ToString("N")) + ");");
    }

    public async Task NewPageAsync()
    {
        if (await EnsureReadyAsync() && _browser?.CoreWebView2 is not null && await SaveAsync())
            await _browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('#studio-create-page')?.click();");
    }

    /// <summary>Previews the native HTML/Razor catalog without changing either saved Page model.</summary>
    public async Task<StudioAuthoringLibraryItem?> PreviewCatalogElementAsync(string? descriptorId)
    {
        var version = ++_catalogPreviewVersion;
        var item = descriptorId is null ? null : _libraryService.DescribeCatalogElement(descriptorId);
        if (!await EnsureReadyAsync() || version != _catalogPreviewVersion || _browser?.CoreWebView2 is null) return null;
        await _browser.CoreWebView2.ExecuteScriptAsync("window.__studioAuthoring?.previewCatalogElement?.("
            + JsonSerializer.Serialize(item, ContentJson.Options) + ");");
        return item;
    }

    /// <summary>Uses the writer's save/conflict guard and waits for its actual asynchronous insertion result.</summary>
    public async Task<bool> CommitCatalogElementAsync(string descriptorId)
    {
        var item = _libraryService.DescribeCatalogElement(descriptorId);
        if (!item.Compatible) { StatusChanged?.Invoke(item.Reason ?? "This element cannot be added to a Page."); return false; }
        if (!await EnsureReadyAsync() || _browser?.CoreWebView2 is null) return false;
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _catalogCommits[requestId] = completion;
        try
        {
            var result = JsonSerializer.Serialize(new { type = "studio:catalog-commit-result", requestId }, ContentJson.Options);
            await _browser.CoreWebView2.ExecuteScriptAsync("Promise.resolve(window.__studioAuthoring?.commitCatalogElement?.("
                + JsonSerializer.Serialize(item.Id) + ")).then(success=>window.chrome.webview.postMessage({..." + result
                + ",success:success===true})).catch(()=>window.chrome.webview.postMessage({..." + result + ",success:false}));");
            return await completion.Task.WaitAsync(TimeSpan.FromMinutes(3), _lifetime.Token);
        }
        catch (Exception error) when (error is TimeoutException or OperationCanceledException or InvalidOperationException)
        { if (!_disposed) StatusChanged?.Invoke("The Page could not confirm its element insertion: " + error.Message); return false; }
        finally { _catalogCommits.Remove(requestId); }
    }

    /// <summary>Reconnects this view after an explicit foundation save upgrades its owned gateway source.</summary>
    public async Task ReloadProjectHostAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(StudioHyperlibAuthoringView));
        // Staging already flushes content before editing the foundation. Never reload a live draft.
        if (HasUnsavedContent && !await SaveAsync())
            throw new InvalidOperationException("Save or recover the current Page draft before reconnecting its project host.");
        _pendingNode = CurrentNodeId ?? _pendingNode;
        _ready = false; _navigating = false;
        SetWorkspaceChromeReady(false);
        _readyCompletion?.TrySetResult(false);
        _endpoint = null;
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingText.Text = "Reopening this project’s updated Page templates…";
        await InitializeAsync();
        if (!await EnsureReadyAsync())
            throw new InvalidOperationException("The updated project host is still opening. Use Try again when it is ready; your saved content and recovery draft remain in this project.");
    }

    public async Task RefreshFoundationAsync()
    {
        if (!await EnsureReadyAsync() || _browser?.CoreWebView2 is null)
            throw new InvalidOperationException("The content tools are not ready to refresh the Page foundation.");
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _flushes[requestId] = completion;
        try
        {
            await _browser.CoreWebView2.ExecuteScriptAsync("window.__studioAuthoring?.refreshFoundation(" + JsonSerializer.Serialize(requestId) + ");");
            if (!await completion.Task.WaitAsync(TimeSpan.FromSeconds(30), _lifetime.Token))
                throw new InvalidOperationException("The content tools could not refresh the shared Page templates. Save or recover the current draft before retrying.");
        }
        finally { _flushes.Remove(requestId); }
    }

    private async Task<bool> EnsureReadyAsync()
    {
        if (_disposed) return false;
        if (!_ready) await InitializeAsync();
        if (_ready) return true;
        if (_readyCompletion is not { } pending) return false;
        try { return await pending.Task.WaitAsync(TimeSpan.FromSeconds(30), _lifetime.Token) && _ready; }
        catch (Exception error) when (error is TimeoutException or OperationCanceledException)
        { if (!_disposed) StatusChanged?.Invoke("The Page tools are still opening. Try again when they are ready."); return false; }
    }

    private void OpenExternalLink(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var link) && link.Scheme is "https" or "http")
        {
            try { Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
            { StatusChanged?.Invoke("The link could not open: " + error.Message); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _readyCompletion?.TrySetResult(false);
        _lifetime.Cancel();
        foreach (var completion in _flushes.Values) completion.TrySetResult(false);
        foreach (var completion in _catalogCommits.Values) completion.TrySetResult(false);
        _flushes.Clear();
        _browser?.Dispose();
        _libraryService.Dispose();
        _gateway.Dispose();
        // A hidden/rebuilt pane retains its project-local draft; closing never deletes it.
    }
}
