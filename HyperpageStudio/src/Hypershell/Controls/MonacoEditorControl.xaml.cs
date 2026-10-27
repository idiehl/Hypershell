using Hypershell.Models;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Hypershell.Controls;

public delegate void EditorCommandCompletedEventHandler(
    object sender,
    EditorCommandCompletedEventArgs eventArgs);

public sealed class EditorCommandCompletedEventArgs : RoutedEventArgs
{
    public EditorCommandCompletedEventArgs(
        RoutedEvent routedEvent,
        object source,
        string command,
        bool succeeded,
        string message)
        : base(routedEvent, source)
    {
        Command = command;
        Succeeded = succeeded;
        Message = message;
    }

    public string Command { get; }
    public bool Succeeded { get; }
    public string Message { get; }
}

public partial class MonacoEditorControl : UserControl
{
    private const string EditorHostName = "hypershell-editor.local";
    private static readonly Lazy<Task<CoreWebView2Environment>> SharedEnvironment =
        new(CreateEnvironmentAsync);

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(MonacoEditorControl),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnEditorStatePropertyChanged));

    public static readonly DependencyProperty LanguageIdProperty =
        DependencyProperty.Register(
            nameof(LanguageId),
            typeof(string),
            typeof(MonacoEditorControl),
            new PropertyMetadata("plaintext", OnEditorStatePropertyChanged));

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(
            nameof(IsReadOnly),
            typeof(bool),
            typeof(MonacoEditorControl),
            new PropertyMetadata(false, OnEditorStatePropertyChanged));

    public static readonly DependencyProperty ViewStateProperty =
        DependencyProperty.Register(
            nameof(ViewState),
            typeof(string),
            typeof(MonacoEditorControl),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnEditorStatePropertyChanged));

    public static readonly DependencyProperty EditorOptionsProperty =
        DependencyProperty.Register(
            nameof(EditorOptions),
            typeof(DocumentEditorSettings),
            typeof(MonacoEditorControl),
            new PropertyMetadata(
                new DocumentEditorSettings(),
                OnEditorStatePropertyChanged));

    public static readonly RoutedEvent SaveRequestedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(SaveRequested),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(MonacoEditorControl));

    public static readonly RoutedEvent SaveAsRequestedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(SaveAsRequested),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(MonacoEditorControl));

    public static readonly RoutedEvent NewDocumentRequestedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(NewDocumentRequested),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(MonacoEditorControl));

    public static readonly RoutedEvent OpenDocumentRequestedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(OpenDocumentRequested),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(MonacoEditorControl));

    public static readonly RoutedEvent FormatRequestedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(FormatRequested),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(MonacoEditorControl));

    public static readonly RoutedEvent EditorCommandCompletedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(EditorCommandCompleted),
            RoutingStrategy.Bubble,
            typeof(EditorCommandCompletedEventHandler),
            typeof(MonacoEditorControl));

    private bool _isInitializing;
    private bool _isEditorReady;
    private bool _isApplyingWebMessage;
    private bool _isDisposed;

    public MonacoEditorControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public event RoutedEventHandler SaveRequested
    {
        add => AddHandler(SaveRequestedEvent, value);
        remove => RemoveHandler(SaveRequestedEvent, value);
    }

    public event RoutedEventHandler SaveAsRequested
    {
        add => AddHandler(SaveAsRequestedEvent, value);
        remove => RemoveHandler(SaveAsRequestedEvent, value);
    }

    public event RoutedEventHandler NewDocumentRequested
    {
        add => AddHandler(NewDocumentRequestedEvent, value);
        remove => RemoveHandler(NewDocumentRequestedEvent, value);
    }

    public event RoutedEventHandler OpenDocumentRequested
    {
        add => AddHandler(OpenDocumentRequestedEvent, value);
        remove => RemoveHandler(OpenDocumentRequestedEvent, value);
    }

    public event RoutedEventHandler FormatRequested
    {
        add => AddHandler(FormatRequestedEvent, value);
        remove => RemoveHandler(FormatRequestedEvent, value);
    }

    public event EditorCommandCompletedEventHandler EditorCommandCompleted
    {
        add => AddHandler(EditorCommandCompletedEvent, value);
        remove => RemoveHandler(EditorCommandCompletedEvent, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string LanguageId
    {
        get => (string)GetValue(LanguageIdProperty);
        set => SetValue(LanguageIdProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public string ViewState
    {
        get => (string)GetValue(ViewStateProperty);
        set => SetValue(ViewStateProperty, value);
    }

    public DocumentEditorSettings EditorOptions
    {
        get => (DocumentEditorSettings)GetValue(EditorOptionsProperty);
        set => SetValue(EditorOptionsProperty, value);
    }

    public bool InsertText(string text)
    {
        if (!_isEditorReady || string.IsNullOrEmpty(text))
        {
            return false;
        }

        PostMessage(new Dictionary<string, object?>
        {
            ["type"] = "insertText",
            ["text"] = text
        });
        return true;
    }

    public void FocusEditor()
    {
        if (_isEditorReady)
        {
            PostMessage(new Dictionary<string, object?> { ["type"] = "focus" });
        }
    }

    public void FormatDocument()
    {
        RaiseEvent(new RoutedEventArgs(FormatRequestedEvent, this));
    }

    public void FormatInEditor() => ExecuteEditorCommand("format");

    public void ShowFind() => ExecuteEditorCommand("find");

    public void ShowReplace() => ExecuteEditorCommand("replace");

    public void ShowGoToLine() => ExecuteEditorCommand("goToLine");

    public void ApplyMarkdownFormat(string style)
    {
        if (!_isEditorReady || _isDisposed || string.IsNullOrWhiteSpace(style))
        {
            return;
        }

        PostMessage(new Dictionary<string, object?>
        {
            ["type"] = "markdownFormat",
            ["style"] = style,
            ["requestId"] = Guid.NewGuid().ToString("N")
        });
    }

    private async void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        await InitializeEditorAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        if (EditorWebView.CoreWebView2 is not null)
        {
            EditorWebView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            EditorWebView.CoreWebView2.NavigationStarting -= OnNavigationStarting;
            EditorWebView.CoreWebView2.ProcessFailed -= OnProcessFailed;
        }

        EditorWebView.Dispose();
    }

    private async Task InitializeEditorAsync()
    {
        if (_isInitializing || _isEditorReady || _isDisposed)
        {
            return;
        }

        _isInitializing = true;
        try
        {
            var editorHostDirectory = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "EditorHost");
            var entryPoint = Path.Combine(editorHostDirectory, "index.html");
            if (!File.Exists(entryPoint))
            {
                throw new FileNotFoundException(
                    "The local Monaco editor assets are missing. Run npm install and npm run build in src\\Hypershell.EditorHost.",
                    entryPoint);
            }

            var environment = await SharedEnvironment.Value;
            await EditorWebView.EnsureCoreWebView2Async(environment);
            if (_isDisposed || EditorWebView.CoreWebView2 is null)
            {
                return;
            }

            var core = EditorWebView.CoreWebView2;
            core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsWebMessageEnabled = true;
            core.SetVirtualHostNameToFolderMapping(
                EditorHostName,
                editorHostDirectory,
                CoreWebView2HostResourceAccessKind.DenyCors);
            core.WebMessageReceived += OnWebMessageReceived;
            core.NavigationStarting += OnNavigationStarting;
            core.ProcessFailed += OnProcessFailed;
            core.Navigate($"https://{EditorHostName}/index.html");
        }
        catch (Exception exception)
        {
            EditorStatusText.Text =
                $"The code editor could not start. {exception.Message}";
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void OnNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs eventArgs)
    {
        if (!Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out var uri)
            || !string.Equals(
                uri.Host,
                EditorHostName,
                StringComparison.OrdinalIgnoreCase))
        {
            eventArgs.Cancel = true;
        }
    }

    private void OnProcessFailed(
        object? sender,
        CoreWebView2ProcessFailedEventArgs eventArgs)
    {
        EditorStatusOverlay.Visibility = Visibility.Visible;
        EditorStatusText.Text =
            "The editor process stopped unexpectedly. Close and reopen this document tab to restart it.";
    }

    private void OnWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        try
        {
            using var message = JsonDocument.Parse(eventArgs.WebMessageAsJson);
            if (!message.RootElement.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            switch (typeElement.GetString())
            {
                case "ready":
                    _isEditorReady = true;
                    EditorStatusOverlay.Visibility = Visibility.Collapsed;
                    SendEditorState();
                    break;

                case "change"
                    when message.RootElement.TryGetProperty("text", out var textElement):
                    _isApplyingWebMessage = true;
                    try
                    {
                        SetCurrentValue(TextProperty, textElement.GetString() ?? string.Empty);
                    }
                    finally
                    {
                        _isApplyingWebMessage = false;
                    }
                    break;

                case "viewState"
                    when message.RootElement.TryGetProperty("viewState", out var stateElement):
                    _isApplyingWebMessage = true;
                    try
                    {
                        SetCurrentValue(ViewStateProperty, stateElement.GetRawText());
                    }
                    finally
                    {
                        _isApplyingWebMessage = false;
                    }
                    break;

                case "save":
                    RaiseEvent(new RoutedEventArgs(SaveRequestedEvent, this));
                    break;

                case "saveAs":
                    RaiseEvent(new RoutedEventArgs(SaveAsRequestedEvent, this));
                    break;

                case "newDocument":
                    RaiseEvent(new RoutedEventArgs(NewDocumentRequestedEvent, this));
                    break;

                case "openDocument":
                    RaiseEvent(new RoutedEventArgs(OpenDocumentRequestedEvent, this));
                    break;

                case "formatRequested":
                    RaiseEvent(new RoutedEventArgs(FormatRequestedEvent, this));
                    break;

                case "commandResult":
                    var command = message.RootElement.TryGetProperty(
                        "command",
                        out var commandElement)
                        ? commandElement.GetString() ?? string.Empty
                        : string.Empty;
                    var succeeded = message.RootElement.TryGetProperty(
                        "success",
                        out var successElement)
                        && successElement.ValueKind == JsonValueKind.True;
                    var resultMessage = message.RootElement.TryGetProperty(
                        "message",
                        out var resultMessageElement)
                        ? resultMessageElement.GetString() ?? string.Empty
                        : string.Empty;
                    RaiseEvent(new EditorCommandCompletedEventArgs(
                        EditorCommandCompletedEvent,
                        this,
                        command,
                        succeeded,
                        resultMessage));
                    break;
            }
        }
        catch (JsonException)
        {
        }
    }

    private static void OnEditorStatePropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is MonacoEditorControl editor
            && !editor._isApplyingWebMessage)
        {
            editor.SendEditorState();
        }
    }

    private void SendEditorState()
    {
        if (!_isEditorReady || EditorWebView.CoreWebView2 is null)
        {
            return;
        }

        var options = EditorOptions ?? new DocumentEditorSettings();
        var state = new Dictionary<string, object?>
        {
            ["type"] = "setState",
            ["text"] = Text ?? string.Empty,
            ["language"] = string.IsNullOrWhiteSpace(LanguageId)
                ? "plaintext"
                : LanguageId,
            ["readOnly"] = IsReadOnly,
            ["tabSize"] = options.TabSize,
            ["insertSpaces"] = options.InsertSpaces,
            ["detectIndentation"] = options.DetectIndentation,
            ["wordWrap"] = options.WordWrap,
            ["minimap"] = options.MinimapEnabled,
            ["lineNumbers"] = options.LineNumbersEnabled,
            ["bracketPairGuides"] = options.BracketPairGuidesEnabled,
            ["formatOnType"] = options.FormatOnType,
            ["formatOnPaste"] = options.FormatOnPaste,
            ["fontFamily"] = options.FontFamily,
            ["fontSize"] = options.FontSize
        };
        if (!string.IsNullOrWhiteSpace(ViewState))
        {
            try
            {
                using var viewState = JsonDocument.Parse(ViewState);
                state["viewState"] = viewState.RootElement.Clone();
            }
            catch (JsonException)
            {
            }
        }

        PostMessage(state);
    }

    private void PostMessage(IReadOnlyDictionary<string, object?> message)
    {
        EditorWebView.CoreWebView2?.PostWebMessageAsJson(
            JsonSerializer.Serialize(message));
    }

    private void ExecuteEditorCommand(string command)
    {
        if (!_isEditorReady || _isDisposed)
        {
            return;
        }

        PostMessage(new Dictionary<string, object?>
        {
            ["type"] = "command",
            ["command"] = command,
            ["requestId"] = Guid.NewGuid().ToString("N")
        });
    }

    private static Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        var userDataDirectory = Path.Combine(
            new Hypershell.Services.SettingsService().ApplicationDataDirectory,
            "WebView2");
        Directory.CreateDirectory(userDataDirectory);
        return CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataDirectory);
    }
}
