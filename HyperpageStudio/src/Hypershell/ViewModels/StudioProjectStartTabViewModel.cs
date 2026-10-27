using System.IO;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Media;
using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Services;

namespace Hypershell.ViewModels;

/// <summary>A project start surface occupies an upper Studio tab until its project is opened.</summary>
public sealed class StudioProjectStartTabViewModel : ObservableObject, IWorkspaceTab
{
    private readonly Func<string, Task<bool>> _openProjectAsync;
    private readonly StudioProjectScaffoldService _scaffolder;
    private readonly StudioWebProjectTemplateAdapter _webAdapter;
    private readonly CancellationTokenSource _lifetime = new();
    private StudioProjectKind _selectedKind = StudioProjectKind.Hyperlib;
    private string _projectName = "My Work";
    private string _parentDirectory;
    private string _errorMessage = "";
    private string _statusMessage = "";
    private string? _generatedProjectPath;
    private bool _isBusy;
    private bool _wizardStarted;
    private bool _galleryStarted;
    private bool _startWithTemplate;
    private bool _disposed;
    private string? _compatibilityState;
    private string _compatibilityMessage="";

    public StudioProjectStartTabViewModel(Func<string, Task<bool>> openProjectAsync,
        string? templateLibraryRoot = null, string? defaultParentDirectory = null)
    {
        _openProjectAsync = openProjectAsync ?? throw new ArgumentNullException(nameof(openProjectAsync));
        _scaffolder = new StudioProjectScaffoldService(templateLibraryRoot);
        _parentDirectory = defaultParentDirectory ?? VisualPageProjectService.DefaultProjectsDirectory;
        Wizard = new HyperlibWorkWizardViewModel(templateLibraryRoot);
        _webAdapter = new StudioWebProjectTemplateAdapter(new VisualPageComponentCatalog(), Wizard.TemplateLibraryRoot);
        Gallery = new StudioProjectTemplateGalleryViewModel(Wizard.TemplateLibraryRoot);
        Wizard.PropertyChanged += FoundationChanged;
        Gallery.PropertyChanged += FoundationChanged;
        ChooseHyperlibCommand = new RelayCommand(() => ChooseKind(StudioProjectKind.Hyperlib), () => IsEditable);
        ChooseRazorCommand = new RelayCommand(() => ChooseKind(StudioProjectKind.Razor), () => IsEditable);
        ChooseHtmlCommand = new RelayCommand(() => ChooseKind(StudioProjectKind.Html), () => IsEditable);
        BeginWizardCommand = new RelayCommand(BeginWizard, () => IsEditable && !HasValidationError);
        ChooseScratchCommand = new RelayCommand(() => ChooseStartMethod(false), () => IsEditable);
        ChooseTemplateCommand = new RelayCommand(() => ChooseStartMethod(true), () => IsEditable);
        ContinueCommand = new AsyncRelayCommand(ContinueAsync, () => IsEditable && !HasValidationError);
        ChangeProjectTypeCommand = new RelayCommand(() => { IsWizardVisible = false; IsTemplateGalleryVisible = false; ErrorMessage = ""; }, () => IsEditable);
        CreateCommand = new AsyncRelayCommand(CreateAsync, CanCreate);
    }

    public WorkspaceTabKind Kind => WorkspaceTabKind.VisualPage;
    public string Title => string.IsNullOrWhiteSpace(ProjectName) ? "New project" : ProjectName.Trim();
    public string DisplayTitle => Title;
    public string StudioDisplayTitle => Title;
    public string StudioObjectKind => "Project";
    public string StudioProjectKindLabel => "";
    public string RenameText { get => Title; set { } }
    public bool IsRenaming => false;
    public bool CanRename => false;
    public bool HasUnsavedChanges => (IsWizardVisible || IsTemplateGalleryVisible) && _generatedProjectPath is null;
    public string TabColor => "#24332F";
    public Brush TabBackgroundBrush { get; } = new SolidColorBrush(Color.FromRgb(36, 51, 47));
    public Brush TabForegroundBrush { get; } = new SolidColorBrush(Color.FromRgb(230, 234, 240));
    public string AiLiveSourceId { get; } = "studio-start:" + Guid.NewGuid().ToString("N");
    public bool IsAiLiveAssistEnabled { get; set; }
    public HyperlibWorkWizardViewModel Wizard { get; }
    public StudioProjectTemplateGalleryViewModel Gallery { get; }
    public ICommand ChooseHyperlibCommand { get; }
    public ICommand ChooseRazorCommand { get; }
    public ICommand ChooseHtmlCommand { get; }
    public ICommand BeginWizardCommand { get; }
    public ICommand ChooseScratchCommand { get; }
    public ICommand ChooseTemplateCommand { get; }
    public ICommand ContinueCommand { get; }
    public ICommand ChangeProjectTypeCommand { get; }
    public ICommand CreateCommand { get; }
    public StudioProjectKind SelectedKind => _selectedKind;
    public bool IsHyperlibSelected => SelectedKind == StudioProjectKind.Hyperlib;
    public bool IsRazorSelected => SelectedKind == StudioProjectKind.Razor;
    public bool IsHtmlSelected => SelectedKind == StudioProjectKind.Html;
    public bool ShowFoundationChoices => !IsWizardVisible && !IsTemplateGalleryVisible;
    public bool ShowHyperlibStart => ShowFoundationChoices;
    public bool ShowWebCreate => false;
    public bool ShowCreate => ShowFoundationChoices || IsTemplateGalleryVisible || IsWizardVisible && Wizard.IsReviewStep || _generatedProjectPath is not null;
    public bool IsTemplateSelected => _startWithTemplate;
    public bool IsScratchSelected => !_startWithTemplate;
    public string ContinueLabel => IsTemplateSelected ? "Browse templates" : "Create project";
    public bool ShowBackToSetup => IsWizardVisible || IsTemplateGalleryVisible;
    public string CreateLabel => _generatedProjectPath is not null ? "Open created project" : IsBusy ? "Creating project…" : "Create project";
    public string SelectedKindTitle => SelectedKind switch
    {
        StudioProjectKind.Hyperlib => "Hyperlib Work",
        StudioProjectKind.Razor => "Razor Pages",
        _ => "HTML"
    };
    public string StarterDescription => SelectedKind switch
    {
        StudioProjectKind.Hyperlib => "An editable Work landing page with the complete text, media, and interactive element toolset. Add Pages and shape their content directly on the canvas.",
        StudioProjectKind.Razor => "A complete ASP.NET Core Razor Pages app with a shared layout, responsive styles, navigation, and a working local launch configuration.",
        _ => "A responsive HTML website with shared styles, browser JavaScript, multiple linked pages, and a local preview baseline."
    };

    public string ProjectName
    {
        get => _projectName;
        set
        {
            if (!SetProperty(ref _projectName, value ?? "")) return;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(StudioDisplayTitle));
            if (IsWizardVisible) Wizard.WorkTitle = _projectName;
            Gallery.ProjectTitle = _projectName;
            RefreshValidation();
        }
    }
    public string ParentDirectory
    {
        get => _parentDirectory;
        set { if (SetProperty(ref _parentDirectory, value ?? "")) RefreshValidation(); }
    }
    public string ProjectDirectoryPreview
    {
        get
        {
            try { return Path.Combine(ParentDirectory.Trim(), ProjectName.Trim()); }
            catch (ArgumentException) { return "Choose a project name and location"; }
        }
    }
    public string ValidationMessage
    {
        get
        {
            var nameError = ValidateProjectName(ProjectName);
            if (nameError is not null) return nameError;
            if (string.IsNullOrWhiteSpace(ParentDirectory) || !Path.IsPathFullyQualified(ParentDirectory.Trim()))
                return "Choose an absolute folder path for the project location.";
            if (ParentDirectory.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || ParentDirectory.IndexOfAny(['*', '?']) >= 0)
                return "Enter a project location without invalid path characters or wildcards.";
            try
            {
                var parent = Path.GetFullPath(ParentDirectory.Trim());
                if (File.Exists(parent)) return "The project location must be a folder.";
                var target = Path.Combine(parent, ProjectName.Trim());
                if (_generatedProjectPath is null && (Directory.Exists(target) || File.Exists(target)))
                    return "That project folder already exists. Choose another name or open the existing project.";
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { return "Enter a valid project folder path."; }
            return "";
        }
    }
    public bool HasValidationError => ValidationMessage.Length > 0;
    public string ErrorMessage
    {
        get => _errorMessage;
        private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); }
    }
    public bool HasError => ErrorMessage.Length > 0;
    public string CompatibilityMessage
    {
        get
        {
            if(IsHyperlibSelected||!IsWizardVisible||!Wizard.IsReviewStep||!Wizard.IsValid)return "";
            var state=SelectedKind+Wizard.CaptureDraftState();
            if(_compatibilityState==state)return _compatibilityMessage;
            _compatibilityState=state;
            try{_webAdapter.ValidateWork(Wizard.BuildDefinition());_compatibilityMessage="";}
            catch(Exception error){_compatibilityMessage=error.Message;}
            return _compatibilityMessage;
        }
    }
    public bool HasCompatibilityError=>CompatibilityMessage.Length>0;
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(CreateLabel));
            RefreshCommands();
        }
    }
    public bool IsEditable => !IsBusy && _generatedProjectPath is null;
    public bool IsWizardVisible
    {
        get => _wizardStarted;
        private set
        {
            if (!SetProperty(ref _wizardStarted, value)) return;
            RefreshSurfaces();
        }
    }
    public bool IsTemplateGalleryVisible
    {
        get => _galleryStarted;
        private set { if (SetProperty(ref _galleryStarted, value)) { Gallery.IsActive=value; RefreshSurfaces(); } }
    }
    public string? GeneratedProjectPath => _generatedProjectPath;

    public static string? ValidateProjectName(string? value)
    {
        var name = value?.Trim() ?? "";
        if (name.Length == 0) return "Give your project a name.";
        if (name.Length > 80) return "Use a project name of 80 characters or fewer.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' '))
            return "The project name must also be a valid folder name.";
        if (Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|CLOCK\$|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return "Choose a project name that is not reserved by Windows.";
        return null;
    }

    public void BeginWizard()
    {
        if (IsBusy || HasValidationError) return;
        Wizard.InitializeTitle(ProjectName.Trim());
        Wizard.WorkTitle = ProjectName.Trim();
        IsTemplateGalleryVisible = false;
        IsWizardVisible = true;
    }

    private void ChooseStartMethod(bool template)
    {
        if (!IsEditable || !SetProperty(ref _startWithTemplate, template, nameof(IsTemplateSelected))) return;
        OnPropertyChanged(nameof(IsScratchSelected));
        OnPropertyChanged(nameof(ContinueLabel));
        RefreshCommands();
    }
    public async Task ContinueAsync()
    {
        if (!IsEditable || HasValidationError) return;
        if (!IsTemplateSelected) { await CreateAsync(); return; }
        Gallery.ProjectTitle = ProjectName.Trim();
        IsWizardVisible = false;
        IsTemplateGalleryVisible = true;
    }
    private void FoundationChanged(object? sender, PropertyChangedEventArgs args) => RefreshCommands();
    private void RefreshSurfaces()
    {
        foreach (var name in new[] { nameof(ShowFoundationChoices), nameof(ShowHyperlibStart), nameof(ShowWebCreate), nameof(ShowCreate), nameof(ShowBackToSetup) }) OnPropertyChanged(name);
        RefreshCommands();
    }

    public async Task<bool> OpenExistingProjectAsync(string path)
    {
        if (IsBusy || _disposed) return false;
        ErrorMessage = "";
        IsBusy = true;
        StatusMessage = "Opening project…";
        try
        {
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("Choose an existing project folder.");
            var opened = await _openProjectAsync(Path.GetFullPath(path));
            if (!opened) ErrorMessage = "This folder could not be opened as a project. Choose its project root and try again.";
            return opened;
        }
        catch (Exception ex) { ErrorMessage = ex.Message; return false; }
        finally { IsBusy = false; StatusMessage = ""; }
    }

    public async Task CreateAsync()
    {
        if (!CanCreate() || _disposed) return;
        ErrorMessage = "";
        IsBusy = true;
        try
        {
            if (_generatedProjectPath is null)
            {
                StatusMessage = "Creating your project and reusable page layouts…";
                _generatedProjectPath = await _scaffolder.CreateAsync(new StudioProjectCreationRequest
                {
                    Kind = SelectedKind,
                    Name = ProjectName.Trim(),
                    ParentDirectory = ParentDirectory.Trim(),
                    Work = IsTemplateGalleryVisible ? Gallery.BuildDefinition()
                        : IsWizardVisible ? Wizard.BuildDefinition()
                        : HyperlibWorkProjectService.BuildDirectEditingDefinition(ProjectName.Trim()),
                    CompleteTemplateId = IsTemplateGalleryVisible ? Gallery.Selected?.Id : null
                }, _lifetime.Token);
                OnPropertyChanged(nameof(GeneratedProjectPath));
                OnPropertyChanged(nameof(IsEditable));
                OnPropertyChanged(nameof(ShowCreate));
                OnPropertyChanged(nameof(CreateLabel));
            }
            StatusMessage = "Opening your project…";
            if (!await _openProjectAsync(_generatedProjectPath))
                ErrorMessage = $"Your project was created at {_generatedProjectPath}. Use Open created project to try opening it again.";
        }
        catch (OperationCanceledException) { StatusMessage = "Creation cancelled."; }
        catch (Exception ex)
        {
            ErrorMessage = _generatedProjectPath is null ? ex.Message
                : $"Your project was created at {_generatedProjectPath}, but could not be opened: {ex.Message}";
        }
        finally { IsBusy = false; if (!_disposed) StatusMessage = ""; }
    }

    private bool CanCreate() => !IsBusy && !_disposed && (_generatedProjectPath is not null
        || !HasValidationError && (ShowFoundationChoices && IsScratchSelected
            || IsTemplateGalleryVisible && Gallery.CanCreate
            || IsWizardVisible && Wizard.IsReviewStep && Wizard.IsValid&&!HasCompatibilityError));
    private void ChooseKind(StudioProjectKind kind)
    {
        if (IsBusy || _generatedProjectPath is not null) return;
        _selectedKind = kind;
        Gallery.ProjectKind = kind;
        ErrorMessage = "";
        foreach (var property in new[] { nameof(SelectedKind), nameof(IsHyperlibSelected), nameof(IsRazorSelected),
                     nameof(IsHtmlSelected), nameof(ShowHyperlibStart), nameof(ShowWebCreate), nameof(ShowCreate),
                     nameof(SelectedKindTitle), nameof(StarterDescription), nameof(CreateLabel) }) OnPropertyChanged(property);
        RefreshCommands();
    }
    private void RefreshValidation()
    {
        OnPropertyChanged(nameof(ProjectDirectoryPreview));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationError));
        RefreshCommands();
    }
    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CompatibilityMessage));
        OnPropertyChanged(nameof(HasCompatibilityError));
        foreach (var command in new[] { ChooseHyperlibCommand, ChooseRazorCommand, ChooseHtmlCommand,
                     BeginWizardCommand, ChooseScratchCommand, ChooseTemplateCommand, ChangeProjectTypeCommand }) (command as RelayCommand)?.RaiseCanExecuteChanged();
        (ContinueCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CreateCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(ShowCreate));
    }
    public void BeginRename() { }
    public void CommitRename() { }
    public void CancelRename() { }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Wizard.PropertyChanged -= FoundationChanged;
        Gallery.PropertyChanged -= FoundationChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
