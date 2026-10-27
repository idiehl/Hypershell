using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace Hypershell.ViewModels;

public sealed class ProjectWorkItemsViewModel : ObservableObject
{
    private readonly ProjectWorkItemService _service;
    private ProjectWorkItemScope? _selectedProject;
    private string _statusMessage = string.Empty;
    private bool _isCreating;
    private string _draftTitle = string.Empty;
    private string _draftDescription = string.Empty;

    public ProjectWorkItemsViewModel(ProjectWorkItemService service)
    {
        _service = service;
        AddWorkItemCommand = new RelayCommand(
            BeginCreate,
            () => SelectedProject is not null && !IsCreating);
        SubmitWorkItemCommand = new RelayCommand(SubmitDraft);
        CancelWorkItemCommand = new RelayCommand(CancelDraft);
        RemoveDraftAttachmentCommand = new RelayCommand(parameter =>
        {
            if (parameter is ProjectWorkItemAttachmentViewModel attachment)
            {
                DraftAttachments.Remove(attachment);
            }
        });
        RefreshCommand = new RelayCommand(
            LoadSelectedProject,
            () => SelectedProject is not null);
    }

    public ObservableCollection<ProjectWorkItemScope> Projects { get; } = [];
    public ObservableCollection<ProjectWorkItemViewModel> Items { get; } = [];
    public ObservableCollection<ProjectWorkItemAttachmentViewModel>
        DraftAttachments { get; } = [];

    public RelayCommand AddWorkItemCommand { get; }
    public RelayCommand SubmitWorkItemCommand { get; }
    public RelayCommand CancelWorkItemCommand { get; }
    public RelayCommand RemoveDraftAttachmentCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public ProjectWorkItemScope? SelectedProject
    {
        get => _selectedProject;
        set
        {
            if (!SetProperty(ref _selectedProject, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasProject));
            OnPropertyChanged(nameof(ProjectRoot));
            OnPropertyChanged(nameof(StorePath));
            AddWorkItemCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
            CancelDraft();
            LoadSelectedProject();
        }
    }

    public bool HasProject => SelectedProject is not null;
    public bool HasItems => Items.Count > 0;
    public bool IsEmpty => !HasItems;
    public string ProjectRoot => SelectedProject?.RootDirectory ?? string.Empty;
    public string StorePath => SelectedProject is null
        ? string.Empty
        : _service.GetStorePath(SelectedProject);

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsCreating
    {
        get => _isCreating;
        private set
        {
            if (!SetProperty(ref _isCreating, value))
            {
                return;
            }

            AddWorkItemCommand.RaiseCanExecuteChanged();
        }
    }

    public string DraftTitle
    {
        get => _draftTitle;
        set => SetProperty(ref _draftTitle, value);
    }

    public string DraftDescription
    {
        get => _draftDescription;
        set => SetProperty(ref _draftDescription, value);
    }

    public void UpdateProjectScopes(
        IEnumerable<ProjectDefinition> projects,
        string? preferredProjectId,
        string currentFolder)
    {
        var previousId = SelectedProject?.Id;
        var scopes = new List<ProjectWorkItemScope>();
        foreach (var project in projects)
        {
            var root = project.Directories
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(TryGetAvailableDirectory)
                .FirstOrDefault(path => path is not null);
            if (root is null)
            {
                continue;
            }

            scopes.Add(new ProjectWorkItemScope(
                project.Id,
                string.IsNullOrWhiteSpace(project.Name)
                    ? "Unnamed project"
                    : project.Name.Trim(),
                root,
                IsSavedProject: true));
        }

        var currentRoot = TryGetAvailableDirectory(currentFolder);
        if (currentRoot is not null
            && scopes.All(scope => !PathsEqual(
                scope.RootDirectory,
                currentRoot)))
        {
            var folderName = new DirectoryInfo(currentRoot).Name;
            if (string.IsNullOrWhiteSpace(folderName))
            {
                folderName = currentRoot;
            }

            scopes.Add(new ProjectWorkItemScope(
                ProjectWorkItemService.CreateFolderScopeId(currentRoot),
                $"{folderName} (folder)",
                currentRoot,
                IsSavedProject: false));
        }

        Projects.Clear();
        foreach (var scope in scopes
                     .OrderByDescending(scope => scope.IsSavedProject)
                     .ThenBy(scope => scope.Name, StringComparer.OrdinalIgnoreCase))
        {
            Projects.Add(scope);
        }

        var selected = FindScope(preferredProjectId)
                       ?? FindScope(previousId)
                       ?? (currentRoot is null
                           ? null
                           : Projects.FirstOrDefault(scope => PathsEqual(
                               scope.RootDirectory,
                               currentRoot)))
                       ?? Projects.FirstOrDefault();
        if (ReferenceEquals(_selectedProject, selected))
        {
            LoadSelectedProject();
        }
        else
        {
            SelectedProject = selected;
        }
    }

    public void SelectProject(string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return;
        }

        var scope = FindScope(projectId);
        if (scope is not null)
        {
            SelectedProject = scope;
        }
    }

    public void AddAttachmentFiles(
        ProjectWorkItemViewModel? item,
        IEnumerable<string> filePaths)
    {
        var target = item?.Attachments ?? DraftAttachments;
        foreach (var path in filePaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Where(File.Exists)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (target.Any(attachment => string.Equals(
                    attachment.SourcePath,
                    path,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            target.Add(new ProjectWorkItemAttachmentViewModel(
                new ProjectWorkItemAttachment
                {
                    FileName = Path.GetFileName(path),
                    SourcePath = path
                }));
        }

        StatusMessage = target.Count == 0
            ? "No available attachment files were selected."
            : string.Empty;
    }

    public string ResolveAttachmentPath(
        ProjectWorkItemAttachmentViewModel attachment)
    {
        if (SelectedProject is null)
        {
            return attachment.SourcePath;
        }

        return _service.ResolveAttachmentPath(
            SelectedProject,
            attachment.Export());
    }

    public void MoveItem(
        ProjectWorkItemViewModel source,
        ProjectWorkItemViewModel target)
    {
        var sourceIndex = Items.IndexOf(source);
        var targetIndex = Items.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        Items.Move(sourceIndex, targetIndex);
        if (!TryPersist("Work-item priority updated."))
        {
            Items.Move(targetIndex, sourceIndex);
        }
    }

    public string? CreateAiWorkCycleContext()
    {
        if (SelectedProject is null || Items.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.AppendLine("[HYPERSHELL_PROJECT_WORK_ITEMS]");
        builder.AppendLine($"Project: {SelectedProject.Name}");
        builder.AppendLine($"Tracker: {StorePath}");
        builder.AppendLine(
            "Treat these as an ordered, secondary backlog. Do not derail the user's current request. Opportunistically include an aligned item; starred items are desired within the next one to three work cycles. Never claim an item is complete unless its solution was actually implemented and verified.");
        for (var index = 0; index < Items.Count; index++)
        {
            var item = Items[index];
            builder.Append(index + 1)
                .Append(". ")
                .Append(item.IsStarred ? "[STARRED] " : string.Empty)
                .AppendLine(item.Title);
            builder.AppendLine(item.Description);
            foreach (var attachment in item.Attachments)
            {
                builder.Append("Attachment: ")
                    .AppendLine(ResolveAttachmentPath(attachment));
            }
        }

        builder.AppendLine("[/HYPERSHELL_PROJECT_WORK_ITEMS]");
        return builder.ToString();
    }

    internal void ToggleStar(ProjectWorkItemViewModel item)
    {
        var previous = item.IsStarred;
        item.SetStarred(!previous);
        if (!TryPersist(item.IsStarred
                ? $"Starred {item.Title}."
                : $"Unstarred {item.Title}."))
        {
            item.SetStarred(previous);
        }
    }

    internal bool SaveItem(ProjectWorkItemViewModel item)
    {
        if (!Validate(item.Title, item.Description))
        {
            return false;
        }

        item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (!TryPersist($"Saved {item.Title}."))
        {
            return false;
        }

        item.CommitEdit();
        return true;
    }

    internal void DeleteItem(ProjectWorkItemViewModel item)
    {
        var index = Items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        Items.RemoveAt(index);
        if (!TryPersist($"Deleted {item.Title}."))
        {
            Items.Insert(index, item);
        }
    }

    private void BeginCreate()
    {
        DraftTitle = string.Empty;
        DraftDescription = string.Empty;
        DraftAttachments.Clear();
        IsCreating = true;
        StatusMessage = "Describe the work item, then submit it to the project backlog.";
    }

    private void SubmitDraft()
    {
        if (!Validate(DraftTitle, DraftDescription))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var item = new ProjectWorkItemViewModel(
            this,
            new ProjectWorkItem
            {
                Title = DraftTitle.Trim(),
                Description = DraftDescription.Trim(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Attachments = DraftAttachments
                    .Select(attachment => attachment.Export())
                    .ToList()
            });
        Items.Add(item);
        if (!TryPersist($"Added {item.Title}."))
        {
            Items.Remove(item);
            return;
        }

        CancelDraft();
    }

    private void CancelDraft()
    {
        DraftTitle = string.Empty;
        DraftDescription = string.Empty;
        DraftAttachments.Clear();
        IsCreating = false;
    }

    private bool Validate(string title, string description)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            StatusMessage = "Give the work item a short reference title.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            StatusMessage = "Describe the problem, improvement, or mini-project.";
            return false;
        }

        return true;
    }

    private void LoadSelectedProject()
    {
        Items.Clear();
        if (SelectedProject is null)
        {
            StatusMessage = "Open a project or folder to start a work-item list.";
            RefreshCollectionState();
            return;
        }

        try
        {
            var document = _service.Load(SelectedProject);
            foreach (var item in document.Items)
            {
                Items.Add(new ProjectWorkItemViewModel(this, item));
            }

            StatusMessage = Items.Count == 0
                ? "No items"
                : string.Empty;
        }
        catch (Exception exception) when (exception is InvalidDataException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            StatusMessage = exception.Message;
        }

        RefreshCollectionState();
    }

    private bool TryPersist(string successMessage)
    {
        if (SelectedProject is null)
        {
            StatusMessage = "Select a project before changing its work items.";
            return false;
        }

        try
        {
            var document = new ProjectWorkItemDocument
            {
                ProjectId = SelectedProject.Id,
                ProjectName = SelectedProject.Name,
                Items = Items
                    .Select((item, index) => item.Export(index))
                    .ToList()
            };
            _service.Save(SelectedProject, document);
            for (var index = 0; index < Items.Count; index++)
            {
                Items[index].AcceptPersisted(document.Items[index]);
            }

            StatusMessage = successMessage;
            RefreshCollectionState();
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            StatusMessage = $"The work-item list was not changed: {exception.Message}";
            RefreshCollectionState();
            return false;
        }
    }

    private void RefreshCollectionState()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private ProjectWorkItemScope? FindScope(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : Projects.FirstOrDefault(scope => string.Equals(
                scope.Id,
                id,
                StringComparison.OrdinalIgnoreCase));

    private static string? TryGetAvailableDirectory(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)
                ? null
                : Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or NotSupportedException
                                          or PathTooLongException)
        {
            return null;
        }
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(
            Path.GetFullPath(first)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(second)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}

public sealed class ProjectWorkItemViewModel : ObservableObject
{
    private readonly ProjectWorkItemsViewModel _owner;
    private string _title;
    private string _description;
    private bool _isStarred;
    private bool _isEditing;
    private string _editBackupTitle = string.Empty;
    private string _editBackupDescription = string.Empty;
    private List<ProjectWorkItemAttachment> _editBackupAttachments = [];

    public ProjectWorkItemViewModel(
        ProjectWorkItemsViewModel owner,
        ProjectWorkItem item)
    {
        _owner = owner;
        Id = item.Id;
        _title = item.Title;
        _description = item.Description;
        _isStarred = item.IsStarred;
        CreatedAtUtc = item.CreatedAtUtc;
        UpdatedAtUtc = item.UpdatedAtUtc;
        foreach (var attachment in item.Attachments)
        {
            Attachments.Add(new ProjectWorkItemAttachmentViewModel(attachment));
        }

        ToggleStarCommand = new RelayCommand(() => _owner.ToggleStar(this));
        BeginEditCommand = new RelayCommand(BeginEdit);
        SaveEditCommand = new RelayCommand(() => _owner.SaveItem(this));
        CancelEditCommand = new RelayCommand(CancelEdit);
        RemoveAttachmentCommand = new RelayCommand(parameter =>
        {
            if (parameter is ProjectWorkItemAttachmentViewModel attachment)
            {
                Attachments.Remove(attachment);
            }
        });
        DeleteCommand = new RelayCommand(() => _owner.DeleteItem(this));
    }

    public string Id { get; }
    public ObservableCollection<ProjectWorkItemAttachmentViewModel>
        Attachments { get; } = [];
    public RelayCommand ToggleStarCommand { get; }
    public RelayCommand BeginEditCommand { get; }
    public RelayCommand SaveEditCommand { get; }
    public RelayCommand CancelEditCommand { get; }
    public RelayCommand RemoveAttachmentCommand { get; }
    public RelayCommand DeleteCommand { get; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public bool IsStarred
    {
        get => _isStarred;
        private set
        {
            if (SetProperty(ref _isStarred, value))
            {
                OnPropertyChanged(nameof(StarGlyph));
            }
        }
    }

    public string StarGlyph => IsStarred ? "★" : "☆";

    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (SetProperty(ref _isEditing, value))
            {
                OnPropertyChanged(nameof(IsNotEditing));
            }
        }
    }

    public bool IsNotEditing => !IsEditing;

    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    internal void SetStarred(bool value) => IsStarred = value;

    internal ProjectWorkItem Export(int order = 0) =>
        new()
        {
            Id = Id,
            Title = Title.Trim(),
            Description = Description.Trim(),
            IsStarred = IsStarred,
            Order = order,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = UpdatedAtUtc,
            Attachments = Attachments
                .Select(attachment => attachment.Export())
                .ToList()
        };

    internal void AcceptPersisted(ProjectWorkItem item)
    {
        foreach (var persisted in item.Attachments)
        {
            var attachment = Attachments.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Id,
                    persisted.Id,
                    StringComparison.OrdinalIgnoreCase));
            attachment?.AcceptPersisted(persisted);
        }
    }

    internal void CommitEdit()
    {
        _editBackupAttachments.Clear();
        IsEditing = false;
    }

    private void BeginEdit()
    {
        _editBackupTitle = Title;
        _editBackupDescription = Description;
        _editBackupAttachments = Attachments
            .Select(attachment => attachment.Export())
            .ToList();
        IsEditing = true;
    }

    private void CancelEdit()
    {
        Title = _editBackupTitle;
        Description = _editBackupDescription;
        Attachments.Clear();
        foreach (var attachment in _editBackupAttachments)
        {
            Attachments.Add(new ProjectWorkItemAttachmentViewModel(attachment));
        }

        _editBackupAttachments.Clear();
        IsEditing = false;
    }
}

public sealed class ProjectWorkItemAttachmentViewModel : ObservableObject
{
    private string _fileName;
    private string _relativePath;
    private string _sourcePath;

    public ProjectWorkItemAttachmentViewModel(ProjectWorkItemAttachment attachment)
    {
        Id = attachment.Id;
        _fileName = attachment.FileName;
        _relativePath = attachment.RelativePath;
        _sourcePath = attachment.SourcePath;
    }

    public string Id { get; }
    public string FileName => _fileName;
    public string RelativePath => _relativePath;
    public string SourcePath => _sourcePath;

    public ProjectWorkItemAttachment Export() =>
        new()
        {
            Id = Id,
            FileName = FileName,
            RelativePath = RelativePath,
            SourcePath = SourcePath
        };

    public void AcceptPersisted(ProjectWorkItemAttachment attachment)
    {
        SetProperty(ref _fileName, attachment.FileName, nameof(FileName));
        SetProperty(ref _relativePath, attachment.RelativePath, nameof(RelativePath));
        SetProperty(ref _sourcePath, attachment.SourcePath, nameof(SourcePath));
    }
}
