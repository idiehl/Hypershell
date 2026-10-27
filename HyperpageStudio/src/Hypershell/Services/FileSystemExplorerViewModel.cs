using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Profiles;
using System.Collections.ObjectModel;
using System.IO;

namespace Hypershell.Services;

public sealed class FileSystemExplorerViewModel
    : ObservableObject,
      IDisposable
{
    private readonly WorkspaceFileSystemProviderRegistry _providers;
    private readonly Stack<ExplorerNavigationState> _backHistory = [];
    private readonly CancellationTokenSource _lifetime = new();
    private WorkspaceLocation _defaultLocalLocation;
    private WorkspaceLocation _loadedLocation;
    private string _currentPath;
    private string _statusMessage = string.Empty;
    private bool _showHiddenItems;
    private bool _directoriesFirst = true;
    private bool _hasLoadedState;
    private bool _isApplyingSettings;
    private bool _isBusy;
    private bool _disposed;
    private ProjectDefinition? _selectedProject;
    private ExplorerProjectOption? _selectedProjectOption;
    private ExplorerDeviceOption? _selectedDevice;
    private Task _pendingNavigation = Task.CompletedTask;

    public FileSystemExplorerViewModel(
        ExplorerSettings settings,
        WorkspaceFileSystemProviderRegistry providers)
    {
        _providers = providers;
        _defaultLocalLocation = WorkspaceLocation.Local(
            ResolveDefaultPath(settings.StartupDirectory));
        _loadedLocation = _defaultLocalLocation;
        _currentPath = _loadedLocation.Path;
        Devices.Add(ExplorerDeviceOption.ThisPc(_defaultLocalLocation));
        _selectedDevice = Devices[0];
        GoCommand = new AsyncRelayCommand(
            () => TrackNavigation(GoToCurrentPathAsync),
            () => !IsBusy);
        BackCommand = new AsyncRelayCommand(
            () => TrackNavigation(NavigateBackAsync),
            () => !IsBusy && _backHistory.Count > 0);
        RefreshCommand = new AsyncRelayCommand(
            () => TrackNavigation(RefreshEntriesAsync),
            () => !IsBusy);
        ClearProjectCommand = new AsyncRelayCommand(
            () => TrackNavigation(SelectDefaultViewAsync),
            () => !IsBusy);
        ApplySettings(settings);
    }

    public ObservableCollection<FileSystemEntry> Entries { get; } = [];

    public ObservableCollection<ProjectDefinition> Projects { get; } = [];

    public ObservableCollection<ExplorerProjectOption> ProjectOptions { get; }
        = [];

    public ObservableCollection<ExplorerDeviceOption> Devices { get; } = [];

    public string CurrentPath
    {
        get => _currentPath;
        set => SetProperty(ref _currentPath, value ?? string.Empty);
    }

    public string CurrentWorkspaceUri => _loadedLocation.Identity;

    public WorkspaceLocation CurrentLocation => _loadedLocation;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            GoCommand.RaiseCanExecuteChanged();
            BackCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
            ClearProjectCommand.RaiseCanExecuteChanged();
        }
    }

    public AsyncRelayCommand GoCommand { get; }

    public AsyncRelayCommand BackCommand { get; }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ClearProjectCommand { get; }

    public ProjectDefinition? SelectedProject
    {
        get => _selectedProject;
        private set
        {
            if (!SetProperty(ref _selectedProject, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsProjectViewActive));
            OnPropertyChanged(nameof(ProjectViewLabel));
        }
    }

    public ExplorerProjectOption? SelectedProjectOption
    {
        get => _selectedProjectOption;
        set
        {
            if (_isApplyingSettings)
            {
                SetProperty(ref _selectedProjectOption, value);
                return;
            }

            QueueNavigation(ActivateProjectOptionAsync(
                value ?? GetDefaultOption(),
                recordHistory: true));
        }
    }

    public ExplorerDeviceOption? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            var normalized = value is null
                ? Devices.FirstOrDefault()
                : Devices.FirstOrDefault(candidate => string.Equals(
                    candidate.Id,
                    value.Id,
                    StringComparison.OrdinalIgnoreCase));
            if (normalized is null
                || !SetProperty(ref _selectedDevice, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(IsRemoteDevice));
            RebuildProjectOptions(preferredProjectId: null);
            QueueNavigation(ActivateProjectOptionAsync(
                GetDefaultOption(),
                recordHistory: true));
        }
    }

    public bool IsRemoteDevice => SelectedDevice is { IsLocal: false };

    public bool IsProjectViewActive => SelectedProject is not null;

    public string ProjectViewLabel => SelectedProject is null
        ? "Default"
        : $"{SelectedProject.Name} project";

    public event EventHandler<IReadOnlyList<string>>? PathsRequested;

    public Task WhenIdleAsync() => _pendingNavigation;

    public WorkspaceLocation ResolveUserLocation(string value) =>
        ResolveAddress(value);

    public void ApplySettings(ExplorerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _showHiddenItems = settings.ShowHiddenItems;
        _directoriesFirst = settings.DirectoriesFirst;
        _defaultLocalLocation = WorkspaceLocation.Local(
            ResolveDefaultPath(settings.StartupDirectory));

        _isApplyingSettings = true;
        try
        {
            Projects.Clear();
            foreach (var project in settings.Projects.Select(project =>
                         project.Clone()))
            {
                Projects.Add(project);
            }

            var localDevice = Devices.FirstOrDefault(device => device.IsLocal);
            if (localDevice is not null)
            {
                var replacement = localDevice with
                {
                    DefaultLocation = _defaultLocalLocation
                };
                var index = Devices.IndexOf(localDevice);
                Devices[index] = replacement;
                if (SelectedDevice?.IsLocal == true)
                {
                    _selectedDevice = replacement;
                    OnPropertyChanged(nameof(SelectedDevice));
                }
            }

            RebuildProjectOptions(settings.ActiveProjectId);
        }
        finally
        {
            _isApplyingSettings = false;
        }

        var selectedOption = ProjectOptions.FirstOrDefault(option =>
                                 string.Equals(
                                     option.Project?.Id,
                                     settings.ActiveProjectId,
                                     StringComparison.OrdinalIgnoreCase))
                             ?? GetDefaultOption();
        QueueNavigation(ActivateProjectOptionAsync(
            selectedOption,
            recordHistory: _hasLoadedState));
    }

    public void ConfigureRemoteDevices(
        IEnumerable<RemoteHostSummary> hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        var selectedDeviceId = SelectedDevice?.Id
                               ?? ExplorerDeviceOption.LocalDeviceId;
        var selectedDeviceChanged = false;
        _isApplyingSettings = true;
        try
        {
            for (var index = Devices.Count - 1; index >= 0; index--)
            {
                if (!Devices[index].IsLocal)
                {
                    Devices.RemoveAt(index);
                }
            }

            foreach (var host in hosts
                         .Where(host => !host.IsDisabled)
                         .OrderBy(host => host.DisplayName,
                             StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    Devices.Add(ExplorerDeviceOption.FromHost(host));
                }
                catch (ArgumentException)
                {
                    // A malformed remote root remains visible in Settings for
                    // correction but cannot become an Explorer device.
                }
            }

            _selectedDevice = Devices.FirstOrDefault(device => string.Equals(
                                  device.Id,
                                  selectedDeviceId,
                                  StringComparison.OrdinalIgnoreCase))
                              ?? Devices.First();
            selectedDeviceChanged = !string.Equals(
                _selectedDevice.Id,
                selectedDeviceId,
                StringComparison.OrdinalIgnoreCase);
            OnPropertyChanged(nameof(SelectedDevice));
            OnPropertyChanged(nameof(IsRemoteDevice));
            RebuildProjectOptions(SelectedProject?.Id);
        }
        finally
        {
            _isApplyingSettings = false;
        }

        if (selectedDeviceChanged)
        {
            QueueNavigation(ActivateProjectOptionAsync(
                GetDefaultOption(),
                recordHistory: true));
        }
    }

    public async Task OpenEntryAsync(FileSystemEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsDirectory)
        {
            await NavigateAsync(entry.Location);
            return;
        }

        InsertPaths([entry]);
    }

    public async Task CreateFileAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var provider = _providers.Resolve(_loadedLocation);
        var destination = provider.Combine(_loadedLocation, name);
        await provider.CreateFileAsync(
            destination,
            ReadOnlyMemory<byte>.Empty,
            cancellationToken);
        await RefreshEntriesAsync();
        StatusMessage = $"Created {destination.Identity}.";
    }

    public async Task CreateDirectoryAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var provider = _providers.Resolve(_loadedLocation);
        var destination = provider.Combine(_loadedLocation, name);
        await provider.CreateDirectoryAsync(destination, cancellationToken);
        await RefreshEntriesAsync();
        StatusMessage = $"Created {destination.Identity}.";
    }

    public async Task RenameEntryAsync(
        FileSystemEntry entry,
        string newName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var provider = _providers.Resolve(entry.Location);
        await provider.RenameAsync(entry.Location, newName, cancellationToken);
        await RefreshEntriesAsync();
        StatusMessage = $"Renamed {entry.Name} to {newName}.";
    }

    public async Task MoveEntryAsync(
        FileSystemEntry entry,
        WorkspaceLocation destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(destinationDirectory);
        var provider = _providers.Resolve(entry.Location);
        var destinationProvider = _providers.Resolve(destinationDirectory);
        if (!string.Equals(
                provider.ProviderId,
                destinationProvider.ProviderId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                provider.Authority,
                destinationProvider.Authority,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkspaceFileSystemException(
                WorkspaceFailureKind.Unsupported,
                "Move stays within one device. Use upload or download to transfer between devices.");
        }

        var destination = provider.Combine(
            destinationDirectory,
            entry.Name);
        await provider.MoveAsync(entry.Location, destination, cancellationToken);
        await RefreshEntriesAsync();
        StatusMessage = $"Moved {entry.Name} to {destinationDirectory.Identity}.";
    }

    public async Task DeleteEntryAsync(
        FileSystemEntry entry,
        bool recursive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var provider = _providers.Resolve(entry.Location);
        await provider.DeleteAsync(
            entry.Location,
            new WorkspaceDeleteConfirmation
            {
                ConfirmedLocationIdentity = entry.Location.Identity,
                ConfirmPermanentDeletion = true,
                Recursive = recursive
            },
            cancellationToken);
        await RefreshEntriesAsync();
        StatusMessage = $"Permanently deleted {entry.Location.Identity}.";
    }

    public async Task UploadFileAsync(
        string localPath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(localPath);
        var provider = _providers.Resolve(_loadedLocation);
        var destination = provider.Combine(
            _loadedLocation,
            Path.GetFileName(fullPath));
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            useAsync: true);
        await provider.UploadAsync(
            stream,
            destination,
            overwrite: false,
            cancellationToken: cancellationToken);
        await RefreshEntriesAsync();
        StatusMessage = $"Uploaded {Path.GetFileName(fullPath)}.";
    }

    public async Task DownloadFileAsync(
        FileSystemEntry entry,
        string localPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsDirectory)
        {
            throw new WorkspaceFileSystemException(
                WorkspaceFailureKind.Unsupported,
                "Folder downloads are not available in this operation. Select a file.");
        }

        var fullPath = Path.GetFullPath(localPath);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new WorkspaceFileSystemException(
                            WorkspaceFailureKind.InvalidLocation,
                            "The download path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.hypershell-download");
        var provider = _providers.Resolve(entry.Location);
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             useAsync: true))
            {
                await provider.DownloadAsync(
                    entry.Location,
                    stream,
                    cancellationToken: cancellationToken);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        StatusMessage = $"Downloaded {entry.Name} to {fullPath}.";
    }

    public async Task ToggleExpansionAsync(FileSystemEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsDirectory || IsBusy)
        {
            return;
        }

        var entryIndex = Entries.IndexOf(entry);
        if (entryIndex < 0)
        {
            return;
        }

        if (entry.IsExpanded)
        {
            CollapseDescendants(entryIndex, entry.Depth);
            entry.IsExpanded = false;
            StatusMessage = $"Collapsed {entry.Name}.";
            return;
        }

        IsBusy = true;
        try
        {
            var provider = _providers.Resolve(entry.Location);
            var children = SortEntries(
                    await provider.ListAsync(
                        entry.Location,
                        _lifetime.Token))
                .Where(item => _showHiddenItems || !item.IsHidden)
                .Select(item => ToFileSystemEntry(item, entry.Depth + 1))
                .ToList();
            entry.IsExpanded = true;
            for (var index = 0; index < children.Count; index++)
            {
                Entries.Insert(entryIndex + index + 1, children[index]);
            }

            StatusMessage = children.Count == 0
                ? $"{entry.Name} is empty."
                : $"Expanded {entry.Name} ({children.Count} items).";
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void InsertPaths(IEnumerable<FileSystemEntry> selectedEntries)
    {
        var paths = selectedEntries
            .Select(entry => entry.FullPath)
            .Distinct(
                SelectedDevice?.IsLocal == true
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal)
            .ToList();
        if (paths.Count == 0)
        {
            StatusMessage =
                "Select one or more files or folders to insert their paths.";
            return;
        }

        PathsRequested?.Invoke(this, paths);
        StatusMessage =
            $"Inserted {paths.Count} workspace path{(paths.Count == 1 ? string.Empty : "s")} into the command builder.";
    }

    private async Task GoToCurrentPathAsync()
    {
        try
        {
            var target = ResolveAddress(CurrentPath);
            var device = Devices.FirstOrDefault(candidate =>
                candidate.Matches(target));
            if (device is null)
            {
                StatusMessage =
                    "That workspace device is not enabled in this profile.";
                return;
            }

            if (!ReferenceEquals(SelectedDevice, device))
            {
                _isApplyingSettings = true;
                _selectedDevice = device;
                OnPropertyChanged(nameof(SelectedDevice));
                OnPropertyChanged(nameof(IsRemoteDevice));
                RebuildProjectOptions(preferredProjectId: null);
                _isApplyingSettings = false;
            }

            await NavigateAsync(target);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusMessage = exception.Message;
        }
    }

    private async Task NavigateAsync(WorkspaceLocation location)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var provider = _providers.Resolve(location);
            var normalized = provider.Normalize(location);
            var stat = await provider.StatAsync(
                normalized,
                _lifetime.Token);
            if (stat is not { IsDirectory: true })
            {
                StatusMessage = "That workspace folder does not exist.";
                return;
            }

            if (!IsCurrentState(normalized, projectId: null))
            {
                PushCurrentState();
            }

            SetSelectedProjectOption(GetDefaultOption());
            SetLoadedLocation(normalized);
            await LoadEntriesCoreAsync(normalized);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadEntriesCoreAsync(WorkspaceLocation directory)
    {
        var provider = _providers.Resolve(directory);
        var children = await provider.ListAsync(
            directory,
            _lifetime.Token);
        Entries.Clear();
        foreach (var entry in SortEntries(children)
                     .Where(item => _showHiddenItems || !item.IsHidden))
        {
            Entries.Add(ToFileSystemEntry(entry, 0));
        }

        StatusMessage =
            $"{SelectedDevice?.DisplayName ?? "Workspace"} · {Entries.Count} items";
        BackCommand.RaiseCanExecuteChanged();
    }

    private async Task RefreshEntriesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        CurrentPath = _loadedLocation.Path;
        IsBusy = true;
        try
        {
            if (SelectedProject is null)
            {
                await LoadEntriesCoreAsync(_loadedLocation);
            }
            else
            {
                await LoadProjectEntriesAsync(SelectedProject);
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Entries.Clear();
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadProjectEntriesAsync(ProjectDefinition project)
    {
        Entries.Clear();
        var locations = GetProjectLocations(project, SelectedDevice)
            .DistinctBy(location => location.Identity,
                SelectedDevice?.IsLocal == true
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal)
            .ToList();
        foreach (var location in locations)
        {
            var provider = _providers.Resolve(location);
            var stat = await provider.StatAsync(location, _lifetime.Token);
            if (stat is not { IsDirectory: true })
            {
                continue;
            }

            Entries.Add(ToFileSystemEntry(stat, 0));
        }

        StatusMessage = Entries.Count == 0
            ? $"{project.Name} has no available folders on {SelectedDevice?.DisplayName}."
            : $"{project.Name} · {Entries.Count} project root{(Entries.Count == 1 ? string.Empty : "s")} on {SelectedDevice?.DisplayName}";
        BackCommand.RaiseCanExecuteChanged();
    }

    private async Task ActivateProjectOptionAsync(
        ExplorerProjectOption option,
        bool recordHistory)
    {
        if (_disposed)
        {
            return;
        }

        var normalizedOption = NormalizeProjectOption(option);
        var target = normalizedOption.Project is null
            ? SelectedDevice?.DefaultLocation ?? _defaultLocalLocation
            : ResolveProjectAddressLocation(normalizedOption.Project);
        var targetProjectId = normalizedOption.Project?.Id;
        if (recordHistory && !IsCurrentState(target, targetProjectId))
        {
            PushCurrentState();
        }

        SetSelectedProjectOption(normalizedOption);
        SetLoadedLocation(target);
        IsBusy = true;
        try
        {
            if (normalizedOption.Project is null)
            {
                await LoadEntriesCoreAsync(target);
            }
            else
            {
                await LoadProjectEntriesAsync(normalizedOption.Project);
            }
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            Entries.Clear();
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task NavigateBackAsync()
    {
        if (IsBusy)
        {
            return;
        }

        while (_backHistory.Count > 0)
        {
            var state = _backHistory.Pop();
            if (!WorkspaceLocation.TryParse(state.LocationIdentity, out var location))
            {
                continue;
            }

            var device = Devices.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Id,
                    state.DeviceId,
                    StringComparison.OrdinalIgnoreCase)
                && candidate.Matches(location));
            if (device is null)
            {
                continue;
            }

            _isApplyingSettings = true;
            _selectedDevice = device;
            OnPropertyChanged(nameof(SelectedDevice));
            OnPropertyChanged(nameof(IsRemoteDevice));
            RebuildProjectOptions(state.ProjectId);
            _isApplyingSettings = false;
            var option = string.IsNullOrWhiteSpace(state.ProjectId)
                ? GetDefaultOption()
                : ProjectOptions.FirstOrDefault(candidate => string.Equals(
                    candidate.Project?.Id,
                    state.ProjectId,
                    StringComparison.OrdinalIgnoreCase));
            if (option is null)
            {
                continue;
            }

            SetSelectedProjectOption(option);
            SetLoadedLocation(location);
            IsBusy = true;
            try
            {
                if (option.Project is null)
                {
                    await LoadEntriesCoreAsync(location);
                }
                else
                {
                    await LoadProjectEntriesAsync(option.Project);
                }
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                Entries.Clear();
                StatusMessage = exception.Message;
            }
            finally
            {
                IsBusy = false;
            }

            return;
        }

        BackCommand.RaiseCanExecuteChanged();
    }

    private void PushCurrentState()
    {
        if (!_hasLoadedState || SelectedDevice is null)
        {
            return;
        }

        var state = new ExplorerNavigationState(
            SelectedDevice.Id,
            _loadedLocation.Identity,
            SelectedProject?.Id);
        if (_backHistory.TryPeek(out var previous) && previous == state)
        {
            return;
        }

        _backHistory.Push(state);
        BackCommand.RaiseCanExecuteChanged();
    }

    private bool IsCurrentState(
        WorkspaceLocation location,
        string? projectId) =>
        _hasLoadedState
        && WorkspaceLocation.SameIdentity(_loadedLocation, location)
        && string.Equals(
            SelectedProject?.Id ?? string.Empty,
            projectId ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

    private void RebuildProjectOptions(string? preferredProjectId)
    {
        ProjectOptions.Clear();
        ProjectOptions.Add(ExplorerProjectOption.Default);
        foreach (var project in Projects.Where(project =>
                     GetProjectLocations(project, SelectedDevice).Any()))
        {
            ProjectOptions.Add(new ExplorerProjectOption(
                project.Name,
                project));
        }

        var selected = ProjectOptions.FirstOrDefault(option => string.Equals(
                           option.Project?.Id,
                           preferredProjectId,
                           StringComparison.OrdinalIgnoreCase))
                       ?? GetDefaultOption();
        SetSelectedProjectOption(selected);
    }

    private ExplorerProjectOption NormalizeProjectOption(
        ExplorerProjectOption option)
    {
        if (option.Project is null)
        {
            return GetDefaultOption();
        }

        return ProjectOptions.FirstOrDefault(candidate => string.Equals(
                   candidate.Project?.Id,
                   option.Project.Id,
                   StringComparison.OrdinalIgnoreCase))
               ?? GetDefaultOption();
    }

    private ExplorerProjectOption GetDefaultOption() =>
        ProjectOptions.FirstOrDefault(option => option.IsDefault)
        ?? ExplorerProjectOption.Default;

    private void SetSelectedProjectOption(ExplorerProjectOption option)
    {
        SetProperty(
            ref _selectedProjectOption,
            option,
            nameof(SelectedProjectOption));
        SelectedProject = option.Project;
    }

    private WorkspaceLocation ResolveProjectAddressLocation(
        ProjectDefinition project) =>
        GetProjectLocations(project, SelectedDevice).FirstOrDefault()
        ?? SelectedDevice?.DefaultLocation
        ?? _defaultLocalLocation;

    private static IEnumerable<WorkspaceLocation> GetProjectLocations(
        ProjectDefinition project,
        ExplorerDeviceOption? device)
    {
        if (device is null)
        {
            yield break;
        }

        foreach (var value in project.Directories)
        {
            WorkspaceLocation? location = null;
            if (WorkspaceLocation.TryParse(value, out var parsed))
            {
                location = parsed;
            }
            else if (device.IsLocal)
            {
                try
                {
                    location = WorkspaceLocation.Local(value);
                }
                catch (Exception exception) when (exception is ArgumentException
                                                  or IOException
                                                  or NotSupportedException)
                {
                }
            }

            if (location is not null && device.Matches(location))
            {
                yield return location;
            }
        }
    }

    private WorkspaceLocation ResolveAddress(string value)
    {
        if (WorkspaceLocation.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return SelectedDevice?.IsLocal == false
            ? WorkspaceLocation.Ssh(
                SelectedDevice.Id,
                value)
            : WorkspaceLocation.Local(value);
    }

    private void SetLoadedLocation(WorkspaceLocation location)
    {
        _loadedLocation = location;
        CurrentPath = location.Path;
        _hasLoadedState = true;
        OnPropertyChanged(nameof(CurrentLocation));
        OnPropertyChanged(nameof(CurrentWorkspaceUri));
    }

    private IEnumerable<WorkspaceEntry> SortEntries(
        IEnumerable<WorkspaceEntry> entries)
    {
        var comparer = SelectedDevice?.IsLocal == false
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        return _directoriesFirst
            ? entries.OrderByDescending(entry => entry.IsDirectory)
                .ThenBy(entry => entry.Name, comparer)
            : entries.OrderBy(entry => entry.Name, comparer);
    }

    private static FileSystemEntry ToFileSystemEntry(
        WorkspaceEntry entry,
        int depth) =>
        new()
        {
            Name = entry.Name,
            Location = entry.Location,
            IsDirectory = entry.IsDirectory,
            IsSymbolicLink = entry.IsSymbolicLink,
            Depth = depth
        };

    private static string ResolveDefaultPath(string? configuredPath)
    {
        var resolvedPath = TryResolveExistingDirectory(configuredPath);
        if (resolvedPath is not null)
        {
            return resolvedPath;
        }

        resolvedPath = TryResolveExistingDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (resolvedPath is not null)
        {
            return resolvedPath;
        }

        resolvedPath = TryResolveExistingDirectory(
            Directory.GetCurrentDirectory());
        return resolvedPath
               ?? Path.GetPathRoot(Environment.SystemDirectory)
               ?? "C:\\";
    }

    private static string? TryResolveExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Directory.Exists(path) ? Path.GetFullPath(path) : null;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or NotSupportedException)
        {
            return null;
        }
    }

    private void CollapseDescendants(int entryIndex, int entryDepth)
    {
        var nextIndex = entryIndex + 1;
        while (nextIndex < Entries.Count
               && Entries[nextIndex].Depth > entryDepth)
        {
            Entries.RemoveAt(nextIndex);
        }
    }

    private Task SelectDefaultViewAsync() =>
        ActivateProjectOptionAsync(
            GetDefaultOption(),
            recordHistory: true);

    private void QueueNavigation(Task operation)
    {
        _pendingNavigation = operation;
        _ = ObserveNavigationAsync(operation);
    }

    private Task TrackNavigation(Func<Task> operation)
    {
        var task = operation();
        _pendingNavigation = task;
        return task;
    }

    private async Task ObserveNavigationAsync(Task operation)
    {
        try
        {
            await operation;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    private static bool IsExpected(Exception exception) =>
        exception is WorkspaceFileSystemException
            or ArgumentException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or OperationCanceledException;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}

public sealed record ExplorerDeviceOption(
    string Id,
    string DisplayName,
    WorkspaceLocation DefaultLocation,
    RemoteHostSummary? Host)
{
    public const string LocalDeviceId = "this-pc";

    public bool IsLocal => Host is null;

    public string StateText => IsLocal
        ? string.Empty
        : Host!.HasCredential
            ? "SSH"
            : "SSH · CREDENTIAL REQUIRED";

    public static ExplorerDeviceOption ThisPc(WorkspaceLocation location) =>
        new(LocalDeviceId, ResolveLocalDeviceDisplayName(), location, null);

    public static ExplorerDeviceOption FromHost(RemoteHostSummary host) =>
        new(
            host.Id,
            host.DisplayName,
            WorkspaceLocation.Ssh(host.Id, host.DefaultRoot),
            host);

    public bool Matches(WorkspaceLocation location) =>
        IsLocal
            ? location.IsLocal
            : location.IsSsh
              && string.Equals(
                  Id,
                  location.Authority,
                  StringComparison.OrdinalIgnoreCase);

    private static string ResolveLocalDeviceDisplayName()
    {
        var machineName = Environment.MachineName.Trim();
        return string.IsNullOrWhiteSpace(machineName)
            ? "THIS DEVICE"
            : machineName.Replace('-', ' ');
    }
}

public sealed record ExplorerProjectOption(
    string DisplayName,
    ProjectDefinition? Project)
{
    public static ExplorerProjectOption Default { get; } =
        new("Default", null);

    public bool IsDefault => Project is null;
}

internal sealed record ExplorerNavigationState(
    string DeviceId,
    string LocationIdentity,
    string? ProjectId);
