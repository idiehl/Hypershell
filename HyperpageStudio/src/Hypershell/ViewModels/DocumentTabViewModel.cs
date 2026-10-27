using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Services;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace Hypershell.ViewModels;

public sealed class DocumentTabViewModel : ObservableObject, IWorkspaceTab
{
    private readonly TextDocumentService _documentService;
    private readonly WorkspaceDocumentService _workspaceDocumentService;
    private string _title;
    private string _content;
    private string _filePath;
    private WorkspaceLocation? _workspaceLocation;
    private string _workspaceVersion = string.Empty;
    private string _statusText = string.Empty;
    private string _editorViewState = string.Empty;
    private string _documentProviderId = TextDocumentProvider.ProviderId;
    private string _providerRegistrationId = "core.document.registration.fallback";
    private string _providerDisplayName = "Code and text provider";
    private string _providerSourceText = "Built-in fallback provider";
    private IReadOnlyList<DocumentExportFormat> _exportFormats =
        DocumentExportFormatCatalog.StandardDocumentExports;
    private IReadOnlyList<string> _providerFileExtensions = [];
    private IDocumentProvider? _documentProvider;
    private DocumentEditorSettings _editorSettings;
    private DocumentEncodingKind _encodingKind = DocumentEncodingKind.Utf8;
    private DocumentLineEndingKind _lineEndingKind = DocumentLineEndingKind.CrLf;
    private List<string> _preservedLineEndings = [];
    private bool _hasUnsavedChanges;
    private bool _isApplyingSnapshot;
    private bool _isAiLiveAssistEnabled;
    private DocumentEditorViewMode _editorViewMode = DocumentEditorViewMode.Edit;
    private string _documentStatisticsText = string.Empty;

    private DocumentTabViewModel(
        string title,
        string filePath,
        string content,
        DocumentEditorSettings editorSettings,
        TextDocumentService documentService,
        WorkspaceDocumentService workspaceDocumentService)
    {
        _title = title;
        _filePath = filePath;
        _content = content;
        _editorSettings = editorSettings;
        _documentService = documentService;
        _workspaceDocumentService = workspaceDocumentService;
        SessionId = Guid.NewGuid().ToString("N");
        TabBackgroundBrush = CreateTabBrush(editorSettings.TabColor);
        UpdateDocumentStatistics();
    }

    public static DocumentTabViewModel CreateUntitled(
        NewDocumentRequest request,
        TextDocumentService documentService) =>
        CreateUntitled(
            request,
            documentService,
            WorkspaceDocumentService.CreateLocalOnly());

    public static DocumentTabViewModel CreateUntitled(
        NewDocumentRequest request,
        TextDocumentService documentService,
        WorkspaceDocumentService workspaceDocumentService)
    {
        var initialContent = request.InitialContent ?? string.Empty;
        var document = new DocumentTabViewModel(
            request.FileName,
            string.Empty,
            initialContent,
            request.EditorSettings,
            documentService,
            workspaceDocumentService)
        {
            _encodingKind = request.EncodingKind,
            _lineEndingKind = request.LineEndingKind,
            _hasUnsavedChanges = initialContent.Length > 0
        };
        document.StatusText = initialContent.Length > 0
            ? "Unsaved document · starter template"
            : "Unsaved document";
        return document;
    }

    public static async Task<DocumentTabViewModel> OpenAsync(
        string path,
        TextDocumentService documentService,
        CancellationToken cancellationToken = default,
        string? languageIdOverride = null) =>
        await OpenAsync(
            path,
            documentService,
            WorkspaceDocumentService.CreateLocalOnly(),
            cancellationToken,
            languageIdOverride);

    public static async Task<DocumentTabViewModel> OpenAsync(
        string path,
        TextDocumentService documentService,
        WorkspaceDocumentService workspaceDocumentService,
        CancellationToken cancellationToken = default,
        string? languageIdOverride = null) =>
        await OpenAsync(
            WorkspaceLocation.Local(path),
            documentService,
            workspaceDocumentService,
            cancellationToken,
            languageIdOverride);

    public static async Task<DocumentTabViewModel> OpenAsync(
        WorkspaceLocation location,
        TextDocumentService documentService,
        WorkspaceDocumentService workspaceDocumentService,
        CancellationToken cancellationToken = default,
        string? languageIdOverride = null)
    {
        var snapshot = await workspaceDocumentService.LoadAsync(
            location,
            cancellationToken);
        var sourcePath = snapshot.Location.Path;
        var languageId = string.IsNullOrWhiteSpace(languageIdOverride)
            ? EditorLanguageCatalog.ResolveLanguageId(sourcePath)
            : languageIdOverride;
        var editorSettings = EditorLanguageCatalog.CreateDefaultSettings(
                languageId,
                detectIndentation: true)
            with
            {
                TrimTrailingWhitespaceOnSave = false,
                InsertFinalNewlineOnSave = false
            };
        var document = new DocumentTabViewModel(
            GetWorkspaceFileName(sourcePath),
            snapshot.LocalWorkingPath,
            snapshot.Text,
            editorSettings,
            documentService,
            workspaceDocumentService)
        {
            _encodingKind = snapshot.EncodingKind,
            _lineEndingKind = snapshot.LineEndingKind,
            _workspaceLocation = snapshot.Location,
            _workspaceVersion = snapshot.Version
        };
        if (document.LineEndingKind == DocumentLineEndingKind.Preserve)
        {
            document.CapturePreservedLineEndings(snapshot.Text);
        }

        document.StatusText = snapshot.Location.Identity;
        return document;
    }

    public WorkspaceTabKind Kind => WorkspaceTabKind.Document;
    public string SessionId { get; private set; }
    public string AiLiveSourceId => $"document:{SessionId}";
    public bool IsAiLiveAssistEnabled
    {
        get => _isAiLiveAssistEnabled;
        set => SetProperty(ref _isAiLiveAssistEnabled, value);
    }
    internal bool ExcludeFromSessionRestore { get; private set; }
    public string Title => _title;
    public string DisplayTitle => HasUnsavedChanges ? $"{Title} •" : Title;
    public string RenameText
    {
        get => Title;
        set
        {
        }
    }

    public bool IsRenaming => false;
    public bool CanRename => false;
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (SetProperty(ref _hasUnsavedChanges, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public string TabColor => EditorSettings.TabColor;
    public Brush TabBackgroundBrush { get; }
    public Brush TabForegroundBrush { get; } =
        new SolidColorBrush(Color.FromRgb(230, 237, 243));

    public string Content
    {
        get => _content;
        set
        {
            var content = value ?? string.Empty;
            if (LineEndingKind == DocumentLineEndingKind.Preserve)
            {
                content = ApplyPreservedLineEndings(content);
            }

            if (SetProperty(ref _content, content) && !_isApplyingSnapshot)
            {
                UpdateDocumentStatistics();
                HasUnsavedChanges = true;
                StatusText = !HasFilePath
                    ? "Unsaved document"
                    : $"Unsaved changes · {DisplayPath}";
            }
        }
    }

    public string FilePath
    {
        get => _filePath;
        private set => SetProperty(ref _filePath, value);
    }

    public WorkspaceLocation? WorkspaceLocation => _workspaceLocation;

    public string WorkspaceUri => _workspaceLocation?.Identity ?? string.Empty;

    public string WorkspaceVersion => _workspaceVersion;

    public bool IsRemoteDocument => _workspaceLocation?.IsSsh == true;

    public string DisplayPath => _workspaceLocation?.Identity
                                 ?? FilePath;

    public string DocumentReference => !string.IsNullOrWhiteSpace(WorkspaceUri)
        ? WorkspaceUri
        : !string.IsNullOrWhiteSpace(FilePath)
            ? FilePath
            : Title;

    public string LocalWorkingFilePath => FilePath;

    public bool HasFilePath => _workspaceLocation is not null
                               || !string.IsNullOrWhiteSpace(FilePath);

    public DocumentEditorSettings EditorSettings
    {
        get => _editorSettings;
        private set
        {
            if (SetProperty(ref _editorSettings, value))
            {
                OnPropertyChanged(nameof(LanguageId));
                OnPropertyChanged(nameof(IndentationStatus));
                OnPropertyChanged(nameof(EditorFontFamily));
                OnPropertyChanged(nameof(EditorFontSize));
                OnPropertyChanged(nameof(IsWordWrapEnabled));
                OnPropertyChanged(nameof(AreLineNumbersEnabled));
                OnPropertyChanged(nameof(IsMinimapEnabled));
                OnPropertyChanged(nameof(IsMarkdownDocument));
                OnPropertyChanged(nameof(IsHtmlDocument));
                OnPropertyChanged(nameof(CanPreviewInBrowser));
                OnPropertyChanged(nameof(CanRunDocument));
                UpdateDocumentStatistics();
                if (!IsMarkdownDocument
                    && EditorViewMode != DocumentEditorViewMode.Edit)
                {
                    EditorViewMode = DocumentEditorViewMode.Edit;
                }
            }
        }
    }

    public string LanguageId => EditorSettings.LanguageId;
    public IReadOnlyList<EditorLanguageDefinition> LanguageOptions =>
        EditorLanguageCatalog.Languages;
    public bool IsMarkdownDocument => string.Equals(
        LanguageId,
        "markdown",
        StringComparison.OrdinalIgnoreCase);
    public bool IsHtmlDocument => string.Equals(
        LanguageId,
        "html",
        StringComparison.OrdinalIgnoreCase);
    public bool CanPreviewInBrowser => IsHtmlDocument;
    public bool CanRunDocument => !IsRemoteDocument &&
        CompiledLanguageToolchainRegistry.SupportsLanguage(LanguageId);
    public string EditorFontFamily => EditorSettings.FontFamily;
    public int EditorFontSize => EditorSettings.FontSize;
    public bool IsWordWrapEnabled => EditorSettings.WordWrap;
    public bool AreLineNumbersEnabled => EditorSettings.LineNumbersEnabled;
    public bool IsMinimapEnabled => EditorSettings.MinimapEnabled;
    public string DocumentStatisticsText => _documentStatisticsText;
    public DocumentEditorViewMode EditorViewMode
    {
        get => _editorViewMode;
        private set
        {
            var effectiveValue = IsMarkdownDocument
                ? value
                : DocumentEditorViewMode.Edit;
            if (SetProperty(ref _editorViewMode, effectiveValue))
            {
                OnPropertyChanged(nameof(EditorViewModeLabel));
                OnPropertyChanged(nameof(EditorPaneWidth));
                OnPropertyChanged(nameof(PreviewSplitterWidth));
                OnPropertyChanged(nameof(PreviewPaneWidth));
            }
        }
    }

    public string EditorViewModeLabel => EditorViewMode switch
    {
        DocumentEditorViewMode.Split => "SPLIT",
        DocumentEditorViewMode.Preview => "PREVIEW",
        _ => "EDIT"
    };

    public GridLength EditorPaneWidth => EditorViewMode switch
    {
        DocumentEditorViewMode.Preview => new GridLength(0),
        _ => new GridLength(1, GridUnitType.Star)
    };

    public GridLength PreviewSplitterWidth => EditorViewMode ==
        DocumentEditorViewMode.Split
        ? new GridLength(5)
        : new GridLength(0);

    public GridLength PreviewPaneWidth => EditorViewMode switch
    {
        DocumentEditorViewMode.Edit => new GridLength(0),
        _ => new GridLength(1, GridUnitType.Star)
    };
    public string DocumentProviderId => _documentProviderId;
    public string ProviderRegistrationId => _providerRegistrationId;
    public string ProviderDisplayName => _providerDisplayName;
    public string ProviderStatusText =>
        $"{ProviderDisplayName} · {_providerSourceText}";
    public IReadOnlyList<DocumentExportFormat> ExportFormats => _exportFormats;
    public IReadOnlyList<string> ProviderFileExtensions =>
        _providerFileExtensions;
    public bool CanExport => ExportFormats.Count > 0;
    internal IDocumentProvider? DocumentProvider => _documentProvider;
    public string IndentationStatus =>
        $"{(EditorSettings.InsertSpaces ? "Spaces" : "Tabs")}: {EditorSettings.TabSize}";
    public IReadOnlyList<DocumentEncodingOption> EncodingOptions =>
        DocumentFormatCatalog.EncodingOptions;
    public IReadOnlyList<DocumentLineEndingOption> LineEndingOptions =>
        DocumentFormatCatalog.LineEndingOptions;

    public DocumentEncodingKind EncodingKind
    {
        get => _encodingKind;
        set
        {
            if (SetProperty(ref _encodingKind, value))
            {
                OnPropertyChanged(nameof(EncodingStatus));
                MarkFileFormatChanged();
            }
        }
    }

    public DocumentLineEndingKind LineEndingKind
    {
        get => _lineEndingKind;
        set
        {
            if (SetProperty(ref _lineEndingKind, value))
            {
                if (value == DocumentLineEndingKind.Preserve)
                {
                    CapturePreservedLineEndings(Content);
                }

                OnPropertyChanged(nameof(LineEndingStatus));
                MarkFileFormatChanged();
            }
        }
    }

    public string EncodingStatus =>
        DocumentFormatCatalog.GetEncodingDisplayName(EncodingKind);
    public string LineEndingStatus =>
        DocumentFormatCatalog.GetLineEndingDisplayName(LineEndingKind);

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string EditorViewState
    {
        get => _editorViewState;
        set => SetProperty(ref _editorViewState, value);
    }

    public bool ApplyFormattedContent(string formattedText)
    {
        ArgumentNullException.ThrowIfNull(formattedText);
        var normalizedText = LineEndingKind == DocumentLineEndingKind.Preserve
            ? formattedText
            : Regex.Replace(
                formattedText,
                "\\r\\n|\\r|\\n",
                TextDocumentService.GetLineEndingSequence(LineEndingKind));
        if (string.Equals(Content, normalizedText, StringComparison.Ordinal))
        {
            return false;
        }

        Content = normalizedText;
        return true;
    }

    public void SetEditorViewMode(DocumentEditorViewMode mode) =>
        EditorViewMode = mode;

    public void SetEditorFontFamily(string fontFamily)
    {
        if (string.IsNullOrWhiteSpace(fontFamily)
            || string.Equals(
                EditorSettings.FontFamily,
                fontFamily,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        EditorSettings = EditorSettings with
        {
            FontFamily = fontFamily.Trim()
        };
    }

    public void SetEditorFontSize(int fontSize)
    {
        var effectiveSize = Math.Clamp(fontSize, 10, 32);
        if (EditorSettings.FontSize == effectiveSize)
        {
            return;
        }

        EditorSettings = EditorSettings with
        {
            FontSize = effectiveSize
        };
    }

    public void ToggleWordWrap() =>
        EditorSettings = EditorSettings with
        {
            WordWrap = !EditorSettings.WordWrap
        };

    public void ToggleLineNumbers() =>
        EditorSettings = EditorSettings with
        {
            LineNumbersEnabled = !EditorSettings.LineNumbersEnabled
        };

    public void ToggleMinimap() =>
        EditorSettings = EditorSettings with
        {
            MinimapEnabled = !EditorSettings.MinimapEnabled
        };

    public void ApplyDocumentProvider(ResolvedDocumentProvider resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        _documentProvider = resolution.Provider;
        _documentProviderId = resolution.Provider.Definition.Id;
        _providerRegistrationId = resolution.Registration.Id;
        _providerDisplayName = resolution.Registration.DisplayName;
        _providerSourceText = resolution.Registration.IsBuiltIn
            ? resolution.Registration.StatusMessage
            : $"ContentOnly extension {resolution.Registration.OwnerId}";
        _exportFormats = resolution.Provider.Definition.ExportFormats;
        _providerFileExtensions = resolution.Registration.FileExtensions;
        if (!string.Equals(
                EditorSettings.LanguageId,
                resolution.LanguageId,
                StringComparison.OrdinalIgnoreCase))
        {
            EditorSettings = EditorSettings with
            {
                LanguageId = resolution.LanguageId
            };
        }

        OnPropertyChanged(nameof(DocumentProviderId));
        OnPropertyChanged(nameof(ProviderRegistrationId));
        OnPropertyChanged(nameof(ProviderDisplayName));
        OnPropertyChanged(nameof(ProviderStatusText));
        OnPropertyChanged(nameof(ExportFormats));
        OnPropertyChanged(nameof(ProviderFileExtensions));
        OnPropertyChanged(nameof(CanExport));
    }

    public DocumentReadResult ReadThroughProvider()
    {
        if (_documentProvider is null)
        {
            return new DocumentReadResult
            {
                Succeeded = false,
                Summary = "No document provider is bound to this tab.",
                Diagnostics =
                [
                    new DocumentProviderDiagnostic(
                        DocumentProviderDiagnosticSeverity.Error,
                        "DOCUMENT_PROVIDER_UNBOUND",
                        "The host has not bound a typed document provider to this tab.")
                ]
            };
        }

        return _documentProvider.Read(new DocumentReadRequest(
            Content,
            LanguageId,
            DocumentReference));
    }

    public DocumentMutationResult ReplaceThroughProvider(
        int offset,
        int length,
        string replacement)
    {
        if (_documentProvider is null)
        {
            return UnboundMutationResult();
        }

        var result = _documentProvider.Replace(new DocumentReplaceRequest(
            Content,
            offset,
            length,
            replacement,
            LanguageId,
            DocumentReference));
        if (result.Succeeded)
        {
            Content = result.Content;
        }

        return result;
    }

    public DocumentMutationResult InsertThroughProvider(
        int offset,
        string text)
    {
        if (_documentProvider is null)
        {
            return UnboundMutationResult();
        }

        var result = _documentProvider.Insert(new DocumentInsertRequest(
            Content,
            offset,
            text,
            LanguageId,
            DocumentReference));
        if (result.Succeeded)
        {
            Content = result.Content;
        }

        return result;
    }

    public async Task SaveAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var contentToSave = PrepareContentForSave(Content);
        await _documentService.SaveAsync(
            fullPath,
            contentToSave,
            EncodingKind,
            cancellationToken);

        _isApplyingSnapshot = true;
        try
        {
            if (!string.Equals(_content, contentToSave, StringComparison.Ordinal))
            {
                _content = contentToSave;
                OnPropertyChanged(nameof(Content));
            }

            FilePath = fullPath;
            _workspaceLocation = WorkspaceLocation.Local(fullPath);
            _workspaceVersion = string.Empty;
            _title = Path.GetFileName(fullPath);
            var savedLanguageId = EditorLanguageCatalog.ResolveLanguageId(fullPath);
            if (!string.Equals(
                    savedLanguageId,
                    EditorSettings.LanguageId,
                    StringComparison.OrdinalIgnoreCase))
            {
                EditorSettings = EditorSettings with
                {
                    LanguageId = savedLanguageId
                };
            }

            HasUnsavedChanges = false;
            StatusText = fullPath;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(HasFilePath));
            OnWorkspaceLocationChanged();
        }
        finally
        {
            _isApplyingSnapshot = false;
        }
    }

    public async Task SaveRemoteAsync(
        bool overwriteWithoutVersion,
        CancellationToken cancellationToken = default)
    {
        if (_workspaceLocation is not { IsSsh: true } remoteLocation)
        {
            throw new InvalidOperationException(
                "This document is not attached to a remote workspace file.");
        }

        var contentToSave = PrepareContentForSave(Content);
        var snapshot = await _workspaceDocumentService.SaveAsync(
            remoteLocation,
            contentToSave,
            EncodingKind,
            _workspaceVersion,
            overwriteWithoutVersion,
            cancellationToken);
        ApplyWorkspaceSnapshot(snapshot, contentToSave, hasUnsavedChanges: false);
    }

    public async Task ReloadRemoteAsync(
        CancellationToken cancellationToken = default)
    {
        if (_workspaceLocation is not { IsSsh: true } remoteLocation)
        {
            throw new InvalidOperationException(
                "This document is not attached to a remote workspace file.");
        }

        var snapshot = await _workspaceDocumentService.LoadAsync(
            remoteLocation,
            cancellationToken);
        ApplyWorkspaceSnapshot(snapshot, snapshot.Text, hasUnsavedChanges: false);
    }

    public EditorDocumentSessionEntry CreateSessionEntry() =>
        new()
        {
            SessionId = SessionId,
            Title = Title,
            FilePath = IsRemoteDocument ? string.Empty : FilePath,
            WorkspaceUri = WorkspaceUri,
            WorkspaceVersion = WorkspaceVersion,
            HadUnsavedChanges = HasUnsavedChanges,
            EditorSettings = EditorSettings,
            EditorViewState = EditorViewState,
            EncodingKind = EncodingKind,
            LineEndingKind = LineEndingKind
        };

    public EditorRecoverySnapshot CreateRecoverySnapshot() =>
        new()
        {
            SessionId = SessionId,
            Title = Title,
            FilePath = IsRemoteDocument ? string.Empty : FilePath,
            WorkspaceUri = WorkspaceUri,
            WorkspaceVersion = WorkspaceVersion,
            Content = Content,
            EditorSettings = EditorSettings,
            EditorViewState = EditorViewState,
            EncodingKind = EncodingKind,
            LineEndingKind = LineEndingKind
        };

    public void ExcludeFromFutureSession()
    {
        ExcludeFromSessionRestore = true;
    }

    public static async Task<DocumentTabViewModel?> RestoreAsync(
        EditorDocumentSessionEntry entry,
        EditorRecoverySnapshot? recovery,
        TextDocumentService documentService,
        CancellationToken cancellationToken = default) =>
        await RestoreAsync(
            entry,
            recovery,
            documentService,
            WorkspaceDocumentService.CreateLocalOnly(),
            cancellationToken);

    public static async Task<DocumentTabViewModel?> RestoreAsync(
        EditorDocumentSessionEntry entry,
        EditorRecoverySnapshot? recovery,
        TextDocumentService documentService,
        WorkspaceDocumentService workspaceDocumentService,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParseExact(entry.SessionId, "N", out _))
        {
            return null;
        }

        WorkspaceLocation? location = null;
        if (!string.IsNullOrWhiteSpace(entry.WorkspaceUri))
        {
            WorkspaceLocation.TryParse(entry.WorkspaceUri, out location!);
        }

        if (location is null && !string.IsNullOrWhiteSpace(entry.FilePath))
        {
            try
            {
                location = WorkspaceLocation.Local(entry.FilePath);
            }
            catch (Exception exception) when (exception is IOException
                                              or ArgumentException
                                              or NotSupportedException)
            {
            }
        }

        WorkspaceDocumentSnapshot? diskSnapshot = null;
        if (location is not null)
        {
            try
            {
                diskSnapshot = await workspaceDocumentService.LoadAsync(
                    location,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or ArgumentException
                                              or NotSupportedException
                                              or WorkspaceFileSystemException)
            {
                if (location.IsSsh)
                {
                    diskSnapshot = await workspaceDocumentService.LoadCachedAsync(
                        location,
                        cancellationToken);
                }
            }
        }

        if (diskSnapshot is null && recovery is null)
        {
            return null;
        }

        var editorSettings = recovery?.EditorSettings
                             ?? entry.EditorSettings
                             ?? new DocumentEditorSettings();
        var title = location is not null
            ? GetWorkspaceFileName(location.Path)
            : !string.IsNullOrWhiteSpace(entry.Title)
                ? entry.Title
                : "Untitled.txt";
        var encodingKind = recovery?.EncodingKind
                             ?? diskSnapshot?.EncodingKind
                           ?? entry.EncodingKind;
        if (!Enum.IsDefined(encodingKind))
        {
            encodingKind = DocumentEncodingKind.Utf8;
        }

        var lineEndingKind = recovery?.LineEndingKind
                             ?? diskSnapshot?.LineEndingKind
                             ?? entry.LineEndingKind;
        if (!Enum.IsDefined(lineEndingKind))
        {
            lineEndingKind = DocumentLineEndingKind.CrLf;
        }

        var document = new DocumentTabViewModel(
            title,
            diskSnapshot?.LocalWorkingPath
            ?? (location?.IsLocal == true ? location.Path : string.Empty),
            recovery?.Content ?? diskSnapshot?.Text ?? string.Empty,
            editorSettings,
            documentService,
            workspaceDocumentService)
        {
            SessionId = entry.SessionId,
            _workspaceLocation = location,
            _workspaceVersion = FirstNonEmpty(
                recovery?.WorkspaceVersion,
                entry.WorkspaceVersion,
                diskSnapshot?.Version),
            _encodingKind = encodingKind,
            _lineEndingKind = lineEndingKind,
            _editorViewState = recovery?.EditorViewState
                               ?? entry.EditorViewState
                               ?? string.Empty,
            _hasUnsavedChanges = recovery is not null
        };
        document.StatusText = recovery is not null
            ? location is null
                ? "Recovered unsaved document"
                : $"Recovered unsaved changes · {location.Identity}"
            : location is null
                ? "Unsaved document"
                : diskSnapshot?.IsCachedFallback == true
                    ? $"Offline cached copy · {location.Identity}"
                    : location.Identity;
        if (document.LineEndingKind == DocumentLineEndingKind.Preserve)
        {
            document.CapturePreservedLineEndings(document.Content);
        }

        return document;
    }

    public void BeginRename()
    {
    }

    public void CommitRename()
    {
    }

    public void CancelRename()
    {
    }

    public void Dispose()
    {
    }

    private void UpdateDocumentStatistics()
    {
        var lineCount = Content.Length == 0
            ? 1
            : 1 + Content.Count(character => character == '\n');
        var wordCount = 0;
        var insideWord = false;
        foreach (var character in Content)
        {
            var isWordCharacter = char.IsLetterOrDigit(character)
                                  || character is '\'' or '\u2019';
            if (isWordCharacter && !insideWord)
            {
                wordCount++;
            }

            insideWord = isWordCharacter;
        }

        _documentStatisticsText = IsMarkdownDocument
            ? $"{wordCount:N0} words · {Math.Max(1, (int)Math.Ceiling(wordCount / 220d))} min read · {Content.Length:N0} characters"
            : $"{lineCount:N0} lines · {Content.Length:N0} characters";
        OnPropertyChanged(nameof(DocumentStatisticsText));
    }

    private string PrepareContentForSave(string content)
    {
        var result = LineEndingKind == DocumentLineEndingKind.Preserve
            ? content
            : Regex.Replace(
                content,
                "\\r\\n|\\r|\\n",
                TextDocumentService.GetLineEndingSequence(LineEndingKind));
        result = EditorSettings.TrimTrailingWhitespaceOnSave
            ? Regex.Replace(result, @"[ \t]+(?=\r\n|\r|\n|$)", string.Empty)
            : result;
        if (EditorSettings.InsertFinalNewlineOnSave
            && result.Length > 0
            && result[^1] is not '\r' and not '\n')
        {
            var preferredLineEnding = LineEndingKind == DocumentLineEndingKind.Preserve
                ? TextDocumentService.DetectLineEnding(result)
                : LineEndingKind;
            result += TextDocumentService.GetLineEndingSequence(preferredLineEnding);
        }

        return result;
    }

    private void MarkFileFormatChanged()
    {
        if (_isApplyingSnapshot)
        {
            return;
        }

        HasUnsavedChanges = true;
        StatusText = !HasFilePath
            ? "Unsaved document format"
            : $"Unsaved format changes · {DisplayPath}";
    }

    private void ApplyWorkspaceSnapshot(
        WorkspaceDocumentSnapshot snapshot,
        string content,
        bool hasUnsavedChanges)
    {
        _isApplyingSnapshot = true;
        try
        {
            _workspaceLocation = snapshot.Location;
            _workspaceVersion = snapshot.Version;
            FilePath = snapshot.LocalWorkingPath;
            _title = GetWorkspaceFileName(snapshot.Location.Path);
            if (!string.Equals(_content, content, StringComparison.Ordinal))
            {
                _content = content;
                OnPropertyChanged(nameof(Content));
                UpdateDocumentStatistics();
            }

            _encodingKind = snapshot.EncodingKind;
            _lineEndingKind = snapshot.LineEndingKind;
            if (_lineEndingKind == DocumentLineEndingKind.Preserve)
            {
                CapturePreservedLineEndings(_content);
            }

            HasUnsavedChanges = hasUnsavedChanges;
            StatusText = snapshot.Location.Identity;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(EncodingKind));
            OnPropertyChanged(nameof(EncodingStatus));
            OnPropertyChanged(nameof(LineEndingKind));
            OnPropertyChanged(nameof(LineEndingStatus));
            OnWorkspaceLocationChanged();
        }
        finally
        {
            _isApplyingSnapshot = false;
        }
    }

    private void OnWorkspaceLocationChanged()
    {
        OnPropertyChanged(nameof(WorkspaceLocation));
        OnPropertyChanged(nameof(WorkspaceUri));
        OnPropertyChanged(nameof(WorkspaceVersion));
        OnPropertyChanged(nameof(IsRemoteDocument));
        OnPropertyChanged(nameof(DisplayPath));
        OnPropertyChanged(nameof(DocumentReference));
        OnPropertyChanged(nameof(LocalWorkingFilePath));
        OnPropertyChanged(nameof(HasFilePath));
        OnPropertyChanged(nameof(CanRunDocument));
    }

    private static string GetWorkspaceFileName(string providerPath)
    {
        var normalized = providerPath.TrimEnd('/', '\\');
        var separator = Math.Max(
            normalized.LastIndexOf('/'),
            normalized.LastIndexOf('\\'));
        return separator >= 0
            ? normalized[(separator + 1)..]
            : normalized;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
        ?? string.Empty;

    private DocumentMutationResult UnboundMutationResult() =>
        new()
        {
            Succeeded = false,
            Content = Content,
            Message = "No document provider is bound to this tab.",
            Diagnostics =
            [
                new DocumentProviderDiagnostic(
                    DocumentProviderDiagnosticSeverity.Error,
                    "DOCUMENT_PROVIDER_UNBOUND",
                    "The host has not bound a typed document provider to this tab.")
            ]
        };

    private void CapturePreservedLineEndings(string content)
    {
        _preservedLineEndings = Regex.Matches(content, "\\r\\n|\\r|\\n")
            .Select(match => match.Value)
            .ToList();
    }

    private string ApplyPreservedLineEndings(string content)
    {
        if (_preservedLineEndings.Count == 0)
        {
            CapturePreservedLineEndings(content);
            return content;
        }

        var fallback = ResolvePreservedLineEndingFallback();
        var endingIndex = 0;
        var appliedEndings = new List<string>();
        var result = Regex.Replace(
            content,
            "\\r\\n|\\r|\\n",
            _ =>
            {
                var ending = endingIndex < _preservedLineEndings.Count
                    ? _preservedLineEndings[endingIndex]
                    : fallback;
                endingIndex++;
                appliedEndings.Add(ending);
                return ending;
            });
        _preservedLineEndings = appliedEndings;
        return result;
    }

    private string ResolvePreservedLineEndingFallback()
    {
        var crLfCount = _preservedLineEndings.Count(ending => ending == "\r\n");
        var lfCount = _preservedLineEndings.Count(ending => ending == "\n");
        var crCount = _preservedLineEndings.Count(ending => ending == "\r");
        if (lfCount > crLfCount && lfCount >= crCount)
        {
            return "\n";
        }

        return crCount > crLfCount && crCount > lfCount
            ? "\r"
            : "\r\n";
    }

    private static Brush CreateTabBrush(string color)
    {
        try
        {
            var brush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(color)!);
            brush.Freeze();
            return brush;
        }
        catch
        {
            var brush = new SolidColorBrush(Color.FromRgb(31, 59, 88));
            brush.Freeze();
            return brush;
        }
    }
}
