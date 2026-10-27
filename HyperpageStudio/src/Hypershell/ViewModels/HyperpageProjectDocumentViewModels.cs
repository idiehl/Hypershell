using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Services;
using System.Collections.ObjectModel;
using System.IO;

namespace Hypershell.ViewModels;

public sealed class HyperpageProjectDocumentViewModel : ObservableObject
{
    private VisualPageUserSourceSnapshot _snapshot;
    private string _relativePath;
    private string _content;
    private string _viewState = string.Empty;
    private bool _isRecovered;
    private bool _hasRecoveryConflict;
    private string _recoveryNotice = string.Empty;
    private bool _recoveryBaselineUnavailable;

    public HyperpageProjectDocumentViewModel(
        VisualPageUserSourceSnapshot snapshot,
        string languageId,
        bool isReadOnly)
    {
        _snapshot = snapshot;
        _relativePath = NormalizePath(snapshot.RelativePath);
        _content = snapshot.Content;
        LanguageId = languageId;
        IsReadOnly = isReadOnly;
        EditorOptions = EditorLanguageCatalog.CreateDefaultSettings(
            MonacoLanguageId,
            detectIndentation: true) with
        {
            WordWrap = false,
            MinimapEnabled = true
        };
    }

    public string RelativePath => _relativePath;
    public string ContentId => HyperpageProjectDocumentIds.FromPath(RelativePath);
    public string FileName => Path.GetFileName(RelativePath);
    public string DirectoryName => Path.GetDirectoryName(RelativePath)?
        .Replace('\\', '/') ?? string.Empty;
    public string LanguageId { get; }
    public string MonacoLanguageId => LanguageId.Equals(
        "sass",
        StringComparison.OrdinalIgnoreCase)
        ? "scss"
        : LanguageId;
    public bool IsReadOnly { get; }
    public DocumentEditorSettings EditorOptions { get; }
    internal VisualPageUserSourceSnapshot Snapshot => _snapshot;
    public bool IsRecovered => _isRecovered;
    public bool HasRecoveryConflict => _hasRecoveryConflict;
    public string RecoveryNotice => _recoveryNotice;
    public bool HasRecoveryNotice => !string.IsNullOrEmpty(_recoveryNotice);
    public bool RecoveryBaselineUnavailable => _recoveryBaselineUnavailable;

    public string Title => IsDirty ? FileName + " •" : FileName;
    public string Detail => string.IsNullOrWhiteSpace(DirectoryName)
        ? LanguageDisplayName
        : $"{LanguageDisplayName} · {DirectoryName}";
    public string LanguageDisplayName => LanguageId.ToLowerInvariant() switch
    {
        "sass" => "Sass",
        "less" => "Less",
        _ => EditorLanguageCatalog.GetLanguage(MonacoLanguageId).DisplayName
    };
    public bool IsDirty => !IsReadOnly
                           && (HasRecoveryConflict || !string.Equals(
                               _snapshot.Sha256,
                               VisualPageHashing.Sha256Text(Content),
                               StringComparison.Ordinal));

    public string Content
    {
        get => _content;
        set
        {
            if (IsReadOnly || !SetProperty(ref _content, value ?? string.Empty))
            {
                return;
            }
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(Title));
        }
    }

    public string ViewState
    {
        get => _viewState;
        set => SetProperty(ref _viewState, value ?? string.Empty);
    }

    internal void MarkSaved(string savedContent)
    {
        _snapshot.Content = savedContent;
        _snapshot.Sha256 = VisualPageHashing.Sha256Text(savedContent);
        _recoveryBaselineUnavailable = false;
        SetRecoveryState(false, false, string.Empty);
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(Title));
    }

    internal VisualPageStudioDocumentRecovery CreateRecovery() => new()
    {
        RelativePath = RelativePath,
        Content = Content,
        BaseContent = _snapshot.Content,
        BaseSha256 = _snapshot.Sha256,
        ViewState = ViewState.Length <= VisualPageStudioSessionService.MaximumRecoveredViewStateCharacters ? ViewState : string.Empty,
        BaselineUnavailable = _recoveryBaselineUnavailable
    };

    internal void RestoreRecovery(VisualPageStudioDocumentRecovery recovery, bool sourceMissing, bool sourceChanged)
    {
        _snapshot = new VisualPageUserSourceSnapshot
        {
            RelativePath = RelativePath, Content = recovery.BaseContent, Sha256 = recovery.BaseSha256
        };
        Content = recovery.Content;
        ViewState = recovery.ViewState;
        _recoveryBaselineUnavailable = recovery.BaselineUnavailable;
        SetRecoveryState(true, recovery.BaselineUnavailable || sourceMissing || sourceChanged, recovery.BaselineUnavailable
            ? "Recovered legacy draft: the original source version was not recorded. Copy this text to a new file before replacing source."
            : sourceMissing
            ? "Recovered draft: the source file is missing. Copy this text to a new file; saving will not recreate the missing source."
            : sourceChanged
                ? "Recovered draft: the source changed outside Studio. Copy this text to a new file or reconcile the source before saving."
                : "Recovered unsaved changes from your previous session.");
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(Title));
    }

    private void SetRecoveryState(bool recovered, bool conflict, string notice)
    {
        _isRecovered = recovered;
        _hasRecoveryConflict = conflict;
        _recoveryNotice = notice;
        OnPropertyChanged(nameof(IsRecovered));
        OnPropertyChanged(nameof(HasRecoveryConflict));
        OnPropertyChanged(nameof(RecoveryNotice));
        OnPropertyChanged(nameof(HasRecoveryNotice));
    }

    internal void UpdatePath(string relativePath)
    {
        var normalized = NormalizePath(relativePath);
        if (string.Equals(_relativePath, normalized, StringComparison.Ordinal))
        {
            return;
        }
        _relativePath = normalized;
        _snapshot = new VisualPageUserSourceSnapshot
        {
            RelativePath = normalized,
            Content = Content,
            Sha256 = VisualPageHashing.Sha256Text(Content)
        };
        OnPropertyChanged(nameof(RelativePath));
        OnPropertyChanged(nameof(ContentId));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(DirectoryName));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(IsDirty));
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}

public static class HyperpageProjectDocumentIds
{
    public const string Prefix = "project-file:";

    public static string FromPath(string path) =>
        Prefix + path.Replace('\\', '/').TrimStart('/');

    public static bool TryGetPath(string contentId, out string relativePath)
    {
        if (contentId.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            relativePath = contentId[Prefix.Length..];
            return !string.IsNullOrWhiteSpace(relativePath);
        }
        relativePath = string.Empty;
        return false;
    }
}

public sealed class HyperpageProjectTreeNodeViewModel : ObservableObject
{
    private bool _isExpanded;

    public required string Name { get; init; }
    public required string RelativePath { get; init; }
    public required bool IsDirectory { get; init; }
    public bool IsProjectRoot { get; init; }
    public bool IsProtected { get; init; }
    public long Size { get; init; }
    public string LanguageId { get; init; } = "plaintext";
    public ObservableCollection<HyperpageProjectTreeNodeViewModel> Children
    { get; } = [];

    public string Glyph => IsDirectory ? "\uE8B7" : FileGlyph(LanguageId);
    public string Badge => IsDirectory
        ? Children.Count == 1 ? "1 item" : $"{Children.Count} items"
        : LanguageId.Equals("sass", StringComparison.OrdinalIgnoreCase)
            ? "Sass"
            : LanguageId.Equals("plaintext", StringComparison.OrdinalIgnoreCase)
                ? Path.GetExtension(Name).TrimStart('.').ToUpperInvariant()
                : EditorLanguageCatalog.GetLanguage(LanguageId).DisplayName;
    public string ToolTip => IsProjectRoot
        ? RelativePath
        : string.IsNullOrWhiteSpace(RelativePath)
            ? Name
            : RelativePath + (IsProtected ? " · Studio managed" : string.Empty);
    public bool CanOpen => !IsDirectory;
    public bool CanRename => !IsProjectRoot && !IsProtected;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    private static string FileGlyph(string languageId) =>
        languageId.ToLowerInvariant() switch
        {
            "css" or "scss" or "less" => "\uE8A5",
            "javascript" or "typescript" => "\uE943",
            "csharp" => "\uE943",
            "json" => "\uE8A5",
            "html" => "\uE8A5",
            "markdown" => "\uE8A5",
            _ => "\uE8A5"
        };
}

public sealed class HyperpageAuthoringFieldViewModel : ObservableObject
{
    private string _value;

    public HyperpageAuthoringFieldViewModel(
        HyperpageAuthoringParameter definition,
        Action changed)
    {
        Definition = definition;
        _value = definition.DefaultValue;
        Changed = changed;
    }

    public HyperpageAuthoringParameter Definition { get; }
    public string Id => Definition.Id;
    public string DisplayName => Definition.DisplayName;
    public string Help => Definition.Help;
    public bool IsRequired => Definition.IsRequired;
    public bool IsMultiline => Definition.IsMultiline;
    private Action Changed { get; }

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value ?? string.Empty))
            {
                Changed();
            }
        }
    }
}

public sealed record HyperpageDocumentInsertionRequest(
    string ContentId,
    string Text);
