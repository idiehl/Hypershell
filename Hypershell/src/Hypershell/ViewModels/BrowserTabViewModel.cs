using Hypershell.Infrastructure;
using Hypershell.Models;
using System.IO;
using System.Windows.Media;

namespace Hypershell.ViewModels;

public sealed class BrowserTabViewModel : ObservableObject, IWorkspaceTab
{
    public const string DefaultHomeAddress = "https://www.google.com/";

    private string _title = "New Tab";
    private string _addressText;
    private string _errorMessage = string.Empty;
    private bool _canGoBack;
    private bool _canGoForward;
    private bool _isLoading;
    private bool _isAiLiveAssistEnabled;
    private bool _isDisposed;

    public BrowserTabViewModel(
        string? initialAddress = null,
        string? previewSourcePath = null,
        BrowserNavigationPolicy? navigationPolicy = null,
        string? userDataFolder = null,
        bool isInPrivateModeEnabled = false,
        bool showNavigationToolbar = true)
    {
        SessionId = Guid.NewGuid().ToString("N");
        _addressText = string.IsNullOrWhiteSpace(initialAddress)
            ? DefaultHomeAddress
            : initialAddress.Trim();
        PreviewSourcePath = string.IsNullOrWhiteSpace(previewSourcePath)
            ? string.Empty
            : Path.GetFullPath(previewSourcePath);
        NavigationPolicy = navigationPolicy;
        ShowNavigationToolbar = showNavigationToolbar;
        if (navigationPolicy is not null)
        {
            _title = navigationPolicy.DisplayName;
        }
        BrowserView = new BrowserTabView(
            this,
            userDataFolder,
            isInPrivateModeEnabled);
    }

    public WorkspaceTabKind Kind => WorkspaceTabKind.Browser;
    public string SessionId { get; }
    public string AiLiveSourceId => $"browser:{SessionId}";
    public BrowserTabView BrowserView { get; }
    public BrowserNavigationPolicy? NavigationPolicy { get; }
    public bool IsManagedApp => NavigationPolicy is not null;
    public bool ShowNavigationToolbar { get; }
    internal bool UseDocumentTitle { get; set; }
    public string HomeAddress =>
        NavigationPolicy?.HomeAddress ?? DefaultHomeAddress;
    public string PreviewSourcePath { get; }
    public bool IsLocalPreview => !string.IsNullOrWhiteSpace(PreviewSourcePath);
    public string InitialAddress => _addressText;
    public string Title => _title;
    public string DisplayTitle => Title;
    public string RenameText
    {
        get => Title;
        set
        {
        }
    }

    public bool IsRenaming => false;
    public bool CanRename => false;
    public bool HasUnsavedChanges => false;
    public string TabColor => "#17324A";
    public Brush TabBackgroundBrush { get; } =
        new SolidColorBrush(Color.FromRgb(23, 50, 74));
    public Brush TabForegroundBrush { get; } =
        new SolidColorBrush(Color.FromRgb(230, 237, 243));

    public string AddressText
    {
        get => _addressText;
        set => SetProperty(ref _addressText, value ?? string.Empty);
    }

    public bool CanGoBack
    {
        get => _canGoBack;
        private set => SetProperty(ref _canGoBack, value);
    }

    public bool CanGoForward
    {
        get => _canGoForward;
        private set => SetProperty(ref _canGoForward, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsAiLiveAssistEnabled
    {
        get => _isAiLiveAssistEnabled;
        set => SetProperty(ref _isAiLiveAssistEnabled, value);
    }

    public void BeginNavigation(string? address)
    {
        if (!string.IsNullOrWhiteSpace(address))
        {
            AddressText = address;
        }

        ErrorMessage = string.Empty;
        IsLoading = true;
    }

    public void CompleteNavigation(
        bool succeeded,
        string? errorMessage = null)
    {
        IsLoading = false;
        ErrorMessage = succeeded
            ? string.Empty
            : string.IsNullOrWhiteSpace(errorMessage)
                ? "The page could not be loaded."
                : errorMessage.Trim();
    }

    public void UpdatePageState(
        string? address,
        string? documentTitle,
        bool canGoBack,
        bool canGoForward)
    {
        if (!string.IsNullOrWhiteSpace(address))
        {
            AddressText = address;
        }

        var nextTitle = NavigationPolicy?.DisplayName
                        ?? (string.IsNullOrWhiteSpace(documentTitle)
                            ? TryGetHostName(address) ?? "New Tab"
                            : documentTitle.Trim());
        if (UseDocumentTitle && NavigationPolicy is { } policy
            && !string.IsNullOrWhiteSpace(documentTitle))
        {
            var pageTitle = documentTitle.Trim();
            nextTitle = pageTitle.EndsWith(policy.DisplayName, StringComparison.Ordinal)
                ? pageTitle
                : $"{pageTitle} · {policy.DisplayName}";
        }
        if (SetProperty(ref _title, nextTitle, nameof(Title)))
        {
            OnPropertyChanged(nameof(DisplayTitle));
        }

        CanGoBack = canGoBack;
        CanGoForward = canGoForward;
    }

    public void ReportError(string message)
    {
        IsLoading = false;
        ErrorMessage = message;
    }

    public void RefreshLocalPreview()
    {
        if (!IsLocalPreview)
        {
            return;
        }

        BrowserView.NavigateTo(PreviewSourcePath);
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
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        BrowserView.Dispose();
    }

    private static string? TryGetHostName(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri)
            ? string.IsNullOrWhiteSpace(uri.Host)
                ? uri.Scheme
                : uri.Host
            : null;
}
