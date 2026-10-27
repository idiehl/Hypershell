using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Services;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Windows.Threading;

namespace Hypershell.ViewModels;

public sealed class AtlasMyNetViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan WebProbeCacheDuration =
        TimeSpan.FromSeconds(30);

    private readonly AtlasMyNetAppStore _store;
    private readonly INetworkSocketInventoryService _inventory;
    private readonly AtlasMyNetWebService _web;
    private readonly AtlasMyNetProcessService _processes;
    private readonly DispatcherTimer _refreshTimer;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly ConcurrentDictionary<string, WebProbeCacheEntry> _webProbeCache =
        new(StringComparer.Ordinal);
    private readonly List<NetworkSocketSnapshot> _allSockets = [];
    private List<AtlasMyNetAppDefinition> _definitions = [];
    private string _searchText = string.Empty;
    private bool _showTcp = true;
    private bool _showUdp = true;
    private bool _listenersOnly;
    private bool _isActive;
    private bool _isInitialized;
    private bool _isRefreshing;
    private string _statusMessage = "MyNet is ready.";
    private string _networkSummary = "No socket snapshot yet.";
    private string _lastUpdatedText = string.Empty;
    private bool _isDisposed;
    private int _ownedResourcesDisposed;

    public AtlasMyNetViewModel(
        AtlasMyNetAppStore store,
        INetworkSocketInventoryService inventory,
        AtlasMyNetWebService web,
        AtlasMyNetProcessService processes)
    {
        _store = store;
        _inventory = inventory;
        _web = web;
        _processes = processes;
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += OnRefreshTimerTick;
    }

    public event Action<AtlasMyNetOpenAppRequest>? OpenAppRequested;
    public event Action<NetworkClientLaunchRequest>? NetworkClientRequested;

    public ObservableCollection<AtlasMyNetAppItemViewModel> Apps { get; } = [];
    public ObservableCollection<AtlasMyNetDiscoveredWebApp>
        DiscoveredWebApps
    { get; } = [];
    public ObservableCollection<NetworkSocketSnapshot> NetworkSockets { get; } = [];

    public string AppsPath => _store.AppsPath;

    public bool IsHyperlibSessionRunning(string address) =>
        _processes.GetSessionRuntime(HyperlibMyNetRegistration.AppId) is { StartedThisSession: true } runtime
        && string.Equals(runtime.Url, address, StringComparison.Ordinal);

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                ApplyNetworkFilter();
            }
        }
    }

    public bool ShowTcp
    {
        get => _showTcp;
        set
        {
            if (SetProperty(ref _showTcp, value))
            {
                ApplyNetworkFilter();
            }
        }
    }

    public bool ShowUdp
    {
        get => _showUdp;
        set
        {
            if (SetProperty(ref _showUdp, value))
            {
                ApplyNetworkFilter();
            }
        }
    }

    public bool ListenersOnly
    {
        get => _listenersOnly;
        set
        {
            if (SetProperty(ref _listenersOnly, value))
            {
                ApplyNetworkFilter();
            }
        }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => SetProperty(ref _isRefreshing, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string NetworkSummary
    {
        get => _networkSummary;
        private set => SetProperty(ref _networkSummary, value);
    }

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => SetProperty(ref _lastUpdatedText, value);
    }

    public async Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        try
        {
            _definitions = _store.Load().Select(definition => definition.Clone()).ToList();
            RebuildAppItems();
            _isInitialized = true;
            if (_isActive)
            {
                _refreshTimer.Start();
                await RefreshAsync(forceWebProbe: true);
            }
            else
            {
                StatusMessage = "Open MyNet to start live local discovery.";
            }
        }
        catch (Exception exception) when (exception is InvalidDataException
                                           or IOException
                                           or UnauthorizedAccessException)
        {
            StatusMessage = exception.Message;
        }
    }

    public void SetActive(bool active)
    {
        if (_isDisposed || _isActive == active)
        {
            return;
        }

        _isActive = active;
        if (!_isInitialized)
        {
            return;
        }

        if (active)
        {
            _refreshTimer.Start();
            _ = RefreshAsync(forceWebProbe: false);
        }
        else
        {
            _refreshTimer.Stop();
        }
    }

    public async Task RefreshAsync(bool forceWebProbe)
    {
        if (_isDisposed || !await _refreshGate.WaitAsync(0))
        {
            return;
        }

        IsRefreshing = true;
        try
        {
            var cancellationToken = _disposeCancellation.Token;
            var sockets = await Task.Run(_inventory.Capture, cancellationToken);
            _allSockets.Clear();
            _allSockets.AddRange(sockets);
            ApplyNetworkFilter();
            UpdateNetworkSummary();
            await RefreshAppStatesAsync(sockets, cancellationToken);
            await RefreshWebDiscoveryAsync(
                sockets,
                forceWebProbe,
                cancellationToken);
            LastUpdatedText = $"Updated {DateTimeOffset.Now:HH:mm:ss}";
            StatusMessage = "Live socket inventory updated.";
        }
        catch (OperationCanceledException) when (_isDisposed)
        {
        }
        catch (Exception exception) when (exception is Win32Exception
                                           or SocketException
                                           or IOException
                                           or UnauthorizedAccessException
                                           or InvalidOperationException)
        {
            StatusMessage = $"MyNet refresh failed: {exception.Message}";
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
            if (_isDisposed)
            {
                DisposeOwnedResources();
            }
        }
    }

    public async Task OpenAppAsync(AtlasMyNetAppItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        try
        {
            if (item.Definition.IsNative)
            {
                // Native windows own their lifetime; StartAsync also activates an existing instance.
                if (await StartAppAsync(item) is not null)
                {
                    StatusMessage = $"Opened {item.DisplayName}.";
                }

                return;
            }

            var runtime = item.Runtime;
            var requiresNativeSession = item.Definition.Id == HyperlibMyNetRegistration.AppId
                && item.IsTrusted && HyperlibMyNetRegistration.GetTrustedRoot(item.Definition) is not null;
            if (item.Definition.Launch is not null && (runtime is null
                || requiresNativeSession && (runtime is not { StartedThisSession: true }
                    || !IsHyperlibSessionRunning(runtime.Url))))
            {
                runtime = await StartAppAsync(item);
                if (runtime is null)
                {
                    return;
                }
            }

            var url = runtime?.Url ?? ResolveDefaultUrl(item.Definition);
            OpenAppRequested?.Invoke(new AtlasMyNetOpenAppRequest(
                item.Definition.Clone(),
                url,
                runtime is { StartedThisSession: true } && item.IsTrusted
                    ? HyperlibMyNetRegistration.GetTrustedRoot(item.Definition) : null));
            StatusMessage = $"Opened {item.DisplayName} in a new workspace tab.";
        }
        catch (OperationCanceledException) when (_isDisposed)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                           or UnauthorizedAccessException
                                           or FileNotFoundException
                                           or TimeoutException
                                           or IOException
                                           or Win32Exception)
        {
            item.SetError(exception.Message);
            StatusMessage = exception.Message;
        }
    }

    public async Task<AtlasMyNetAppRuntime?> StartAppAsync(
        AtlasMyNetAppItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.SetStatus(AtlasMyNetAppStatus.Starting, "Starting…", null);
        try
        {
            var runtime = await _processes.StartAsync(
                item.Definition,
                _disposeCancellation.Token);
            item.SetStatus(
                AtlasMyNetAppStatus.Running,
                runtime.StartedThisSession ? "Running · MyNet" : "Running",
                runtime);
            StatusMessage = item.Definition.IsNative
                ? $"{item.DisplayName} is running."
                : $"{item.DisplayName} is ready at {runtime.Url}.";
            return runtime;
        }
        catch (OperationCanceledException) when (_isDisposed)
        {
            return null;
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                           or UnauthorizedAccessException
                                           or FileNotFoundException
                                           or TimeoutException
                                           or IOException
                                           or Win32Exception)
        {
            item.SetError(
                exception.Message,
                _processes.GetSessionRuntime(item.Definition.Id));
            StatusMessage = exception.Message;
            return null;
        }
    }

    public async Task<AtlasMyNetStopResult> RequestAppStopAsync(
        AtlasMyNetAppItemViewModel item)
    {
        if (item.Runtime is not { } runtime)
        {
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.AlreadyStopped,
                $"{item.DisplayName} is already stopped.");
        }

        var result = await _processes.RequestAppStopAsync(
            item.Definition,
            runtime,
            _disposeCancellation.Token);
        ApplyStopResult(item, result);
        return result;
    }

    public AtlasMyNetStopResult ForceStopApp(AtlasMyNetAppItemViewModel item)
    {
        if (item.Runtime is not { } runtime)
        {
            return new AtlasMyNetStopResult(
                AtlasMyNetStopOutcome.AlreadyStopped,
                $"{item.DisplayName} is already stopped.");
        }

        var result = _processes.ForceStop(
            runtime.ProcessId,
            runtime.ExecutablePath,
            runtime.StartedAtUtc);
        ApplyStopResult(item, result);
        return result;
    }

    public async Task<AtlasMyNetStopResult> RequestSocketProcessStopAsync(
        NetworkSocketSnapshot socket) =>
        await _processes.RequestGracefulStopAsync(
            socket.ProcessId,
            socket.ExecutablePath,
            socket.ProcessStartTimeUtc,
            _disposeCancellation.Token);

    public AtlasMyNetStopResult ForceStopSocketProcess(
        NetworkSocketSnapshot socket) =>
        _processes.ForceStop(
            socket.ProcessId,
            socket.ExecutablePath,
            socket.ProcessStartTimeUtc);

    public void OpenDiscoveredApp(AtlasMyNetDiscoveredWebApp app)
    {
        var definition = new AtlasMyNetAppDefinition
        {
            Id = $"discovered:{app.Key}",
            DisplayName = app.DisplayName,
            UrlTemplate = app.Url,
            AllowExternalResources = false
        };
        OpenAppRequested?.Invoke(new AtlasMyNetOpenAppRequest(definition, app.Url));
    }

    public void OpenSocketClient(NetworkSocketSnapshot socket)
    {
        NetworkClientLaunchRequest request;
        if (socket.Transport == NetworkTransport.Udp)
        {
            request = new NetworkClientLaunchRequest(
                NetworkTransport.Udp,
                string.Empty,
                0,
                AutoConnect: false,
                $"Observed local UDP endpoint {socket.LocalEndpoint}; enter a destination to create a new datagram session.");
        }
        else if (socket.RemotePort > 0
                 && !string.IsNullOrWhiteSpace(socket.RemoteAddress)
                 && socket.RemoteAddress is not "0.0.0.0" and not "::")
        {
            request = new NetworkClientLaunchRequest(
                NetworkTransport.Tcp,
                socket.RemoteAddress,
                socket.RemotePort,
                AutoConnect: true,
                $"Creates a new connection; it does not attach to PID {socket.ProcessId}'s existing socket.");
        }
        else
        {
            request = new NetworkClientLaunchRequest(
                NetworkTransport.Tcp,
                AtlasMyNetWebService.NormalizeListenerAddress(
                    socket.LocalAddress,
                    socket.AddressFamily),
                socket.LocalPort,
                AutoConnect: true,
                $"New connection to local listener owned by PID {socket.ProcessId}.");
        }

        NetworkClientRequested?.Invoke(request);
    }

    public void SaveDefinition(AtlasMyNetAppDefinition definition)
    {
        var replacement = AtlasMyNetAppStore.ValidateDefinition(definition);
        _definitions = _store.UpdateDefinitions(latest =>
        {
            var index = latest.FindIndex(app => string.Equals(
                app.Id,
                replacement.Id,
                StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                latest[index] = replacement;
            }
            else
            {
                latest.Add(replacement);
            }
        });

        RebuildAppItems();
        _ = RefreshAsync(forceWebProbe: false);
    }

    public bool RemoveDefinition(AtlasMyNetAppItemViewModel item)
    {
        if (!item.CanRemove)
        {
            return false;
        }

        _definitions = _store.UpdateDefinitions(latest => latest.RemoveAll(app => string.Equals(
            app.Id,
            item.Definition.Id,
            StringComparison.OrdinalIgnoreCase)));
        RebuildAppItems();
        return true;
    }

    public IReadOnlyList<AtlasMyNetAppDefinition> ReadImport(string path) =>
        _store.ReadImport(path);

    public void MergeImport(IEnumerable<AtlasMyNetAppDefinition> imported)
    {
        var validated = imported.Select(AtlasMyNetAppStore.ValidateDefinition).ToList();
        _definitions = _store.UpdateDefinitions(latest =>
        {
            foreach (var definition in validated)
            {
                var clone = definition.Clone();
                var index = latest.FindIndex(app => string.Equals(
                    app.Id,
                    clone.Id,
                    StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    latest[index] = clone;
                }
                else
                {
                    latest.Add(clone);
                }
            }
        });

        RebuildAppItems();
        _ = RefreshAsync(forceWebProbe: false);
    }

    public void Export(string path) => _store.Export(path, _store.Load());

    public void Trust(AtlasMyNetAppItemViewModel item)
    {
        _store.Trust(item.Definition);
        item.RefreshTrust();
        StatusMessage = $"Trusted {item.DisplayName}'s current command definition.";
    }

    public void SaveExternalResourcePermission(
        AtlasMyNetAppItemViewModel item,
        bool allow)
    {
        item.Definition.AllowExternalResources = allow;
        SaveDefinition(item.Definition);
    }

    public bool HasRunningSessionApps() =>
        _processes.HasRunningSessionApps();

    public async Task<IReadOnlyList<AtlasMyNetStopResult>>
        StopSessionAppsForShutdownAsync() =>
        await _processes.StopSessionAppsForShutdownAsync(
            _definitions.ToDictionary(
                definition => definition.Id,
                StringComparer.OrdinalIgnoreCase),
            _disposeCancellation.Token);

    public void CancelShutdown() =>
        _processes.CancelShutdown();

    public async Task<IReadOnlyList<AtlasMyNetStopResult>>
        StopSessionAppsGracefullyAsync() =>
        await _processes.StopSessionAppsGracefullyAsync(
            _definitions.ToDictionary(
                definition => definition.Id,
                StringComparer.OrdinalIgnoreCase),
            _disposeCancellation.Token);

    public void LeaveSessionAppsRunning() =>
        _processes.LeaveSessionAppsRunning();

    private async void OnRefreshTimerTick(object? sender, EventArgs eventArgs) =>
        await RefreshAsync(forceWebProbe: false);

    private async Task RefreshAppStatesAsync(
        IReadOnlyList<NetworkSocketSnapshot> sockets,
        CancellationToken cancellationToken)
    {
        foreach (var item in Apps)
        {
            if (item.Status == AtlasMyNetAppStatus.Starting)
            {
                continue;
            }

            var runtime = await _processes.FindRunningAsync(
                item.Definition,
                sockets,
                cancellationToken);
            if (runtime is not null)
            {
                item.SetStatus(
                    AtlasMyNetAppStatus.Running,
                    runtime.StartedThisSession ? "Running · MyNet" : "Running",
                    runtime);
                continue;
            }

            if (_processes.GetSessionRuntime(item.Definition.Id) is { } launched
                && sockets.Any(socket => socket.ProcessId == launched.ProcessId))
            {
                item.SetStatus(
                    AtlasMyNetAppStatus.Running,
                    "Running · health unavailable · MyNet",
                    launched);
                continue;
            }

            if (item.Definition.Launch is { } launch
                && !File.Exists(launch.Executable))
            {
                item.SetStatus(
                    AtlasMyNetAppStatus.Missing,
                    "Executable missing",
                    null);
            }
            else
            {
                item.SetStatus(AtlasMyNetAppStatus.Stopped, "Stopped", null);
            }
        }
    }

    private async Task RefreshWebDiscoveryAsync(
        IReadOnlyList<NetworkSocketSnapshot> sockets,
        bool force,
        CancellationToken cancellationToken)
    {
        var listeners = sockets.Where(socket => socket.IsListener).ToList();
        var listenerKeys = listeners.Select(socket => socket.Key).ToHashSet();
        foreach (var staleKey in _webProbeCache.Keys
                     .Where(key => !listenerKeys.Contains(key))
                     .ToList())
        {
            _webProbeCache.TryRemove(staleKey, out _);
        }

        using var concurrency = new SemaphoreSlim(8, 8);
        var tasks = listeners.Select(async listener =>
        {
            if (!force
                && _webProbeCache.TryGetValue(listener.Key, out var cached)
                && DateTimeOffset.UtcNow - cached.ProbedAt < WebProbeCacheDuration)
            {
                return;
            }

            await concurrency.WaitAsync(cancellationToken);
            try
            {
                var result = await _web.ProbeListenerAsync(
                    listener,
                    cancellationToken);
                _webProbeCache[listener.Key] = new WebProbeCacheEntry(
                    DateTimeOffset.UtcNow,
                    result);
            }
            finally
            {
                concurrency.Release();
            }
        });
        await Task.WhenAll(tasks);

        var registeredUrls = Apps
            .Where(item => item.Runtime is not null)
            .Select(item => item.Runtime!.Url.TrimEnd('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        ReplaceCollection(
            DiscoveredWebApps,
            _webProbeCache.Values
                .Select(entry => entry.App)
                .Where(app => app is not null
                              && !registeredUrls.Contains(app.Url.TrimEnd('/')))
                .Select(app => app!)
                .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(app => app.Url));
    }

    private void ApplyNetworkFilter()
    {
        var search = SearchText.Trim();
        var filtered = _allSockets.Where(socket =>
            (ShowTcp && socket.Transport == NetworkTransport.Tcp
             || ShowUdp && socket.Transport == NetworkTransport.Udp)
            && (!ListenersOnly || socket.IsListener)
            && (string.IsNullOrWhiteSpace(search)
                || Contains(socket.ProtocolText, search)
                || Contains(socket.State, search)
                || Contains(socket.LocalEndpoint, search)
                || Contains(socket.RemoteEndpoint, search)
                || Contains(socket.ProcessName, search)
                || Contains(socket.OwnerName, search)
                || socket.ProcessId.ToString().Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase)));
        ReconcileNetworkSockets(filtered.ToList());
    }

    private void UpdateNetworkSummary()
    {
        var tcp = _allSockets.Count(socket => socket.Transport == NetworkTransport.Tcp);
        var udp = _allSockets.Count(socket => socket.Transport == NetworkTransport.Udp);
        var listeners = _allSockets.Count(socket => socket.IsListener);
        NetworkSummary = $"{tcp} TCP · {udp} UDP · {listeners} listeners";
    }

    private void RebuildAppItems()
    {
        var priorRuntimeById = Apps
            .Where(item => item.Runtime is not null)
            .ToDictionary(
                item => item.Definition.Id,
                item => item.Runtime!,
                StringComparer.OrdinalIgnoreCase);
        Apps.Clear();
        foreach (var definition in _definitions.OrderBy(definition =>
                     string.Equals(
                         definition.Id,
                         AtlasMyNetAppStore.PingLabAppId,
                         StringComparison.OrdinalIgnoreCase)
                         ? 0
                         : 1).ThenBy(
                     definition => definition.DisplayName,
                     StringComparer.OrdinalIgnoreCase))
        {
            var item = new AtlasMyNetAppItemViewModel(definition.Clone(), _store);
            if (priorRuntimeById.TryGetValue(definition.Id, out var runtime))
            {
                item.SetStatus(
                    AtlasMyNetAppStatus.Running,
                    runtime.StartedThisSession ? "Running · MyNet" : "Running",
                    runtime);
            }

            Apps.Add(item);
        }
    }

    private static string ResolveDefaultUrl(AtlasMyNetAppDefinition definition)
    {
        var port = definition.Ports.FirstOrDefault();
        return definition.UrlTemplate.Replace(
            "{port}",
            port > 0 ? port.ToString() : "80",
            StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyStopResult(
        AtlasMyNetAppItemViewModel item,
        AtlasMyNetStopResult result)
    {
        StatusMessage = result.Message;
        if (result.Outcome is AtlasMyNetStopOutcome.Stopped
            or AtlasMyNetStopOutcome.AlreadyStopped)
        {
            item.SetStatus(AtlasMyNetAppStatus.Stopped, "Stopped", null);
        }
        else if (result.Outcome == AtlasMyNetStopOutcome.Failed)
        {
            item.SetError(result.Message);
        }
    }

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private static void ReplaceCollection<T>(
        ObservableCollection<T> target,
        IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void ReconcileNetworkSockets(
        IReadOnlyList<NetworkSocketSnapshot> next)
    {
        for (var targetIndex = 0; targetIndex < next.Count; targetIndex++)
        {
            var nextRow = next[targetIndex];
            if (targetIndex < NetworkSockets.Count
                && string.Equals(
                    NetworkSockets[targetIndex].Key,
                    nextRow.Key,
                    StringComparison.Ordinal))
            {
                if (NetworkSockets[targetIndex] != nextRow)
                {
                    NetworkSockets[targetIndex] = nextRow;
                }

                continue;
            }

            var existingIndex = -1;
            for (var candidate = targetIndex + 1;
                 candidate < NetworkSockets.Count;
                 candidate++)
            {
                if (string.Equals(
                        NetworkSockets[candidate].Key,
                        nextRow.Key,
                        StringComparison.Ordinal))
                {
                    existingIndex = candidate;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                NetworkSockets.Move(existingIndex, targetIndex);
                if (NetworkSockets[targetIndex] != nextRow)
                {
                    NetworkSockets[targetIndex] = nextRow;
                }
            }
            else
            {
                NetworkSockets.Insert(targetIndex, nextRow);
            }
        }

        while (NetworkSockets.Count > next.Count)
        {
            NetworkSockets.RemoveAt(NetworkSockets.Count - 1);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _disposeCancellation.Cancel();
        if (_refreshGate.Wait(0))
        {
            try
            {
                DisposeOwnedResources();
            }
            finally
            {
                _refreshGate.Release();
            }
        }
    }

    private void DisposeOwnedResources()
    {
        if (Interlocked.Exchange(ref _ownedResourcesDisposed, 1) != 0)
        {
            return;
        }

        _processes.Dispose();
        _web.Dispose();
        _disposeCancellation.Dispose();
    }

    private sealed record WebProbeCacheEntry(
        DateTimeOffset ProbedAt,
        AtlasMyNetDiscoveredWebApp? App);
}

public sealed class AtlasMyNetAppItemViewModel : ObservableObject
{
    private readonly AtlasMyNetAppStore _store;
    private AtlasMyNetAppStatus _status = AtlasMyNetAppStatus.Stopped;
    private string _statusText = "Stopped";
    private string _errorMessage = string.Empty;
    private AtlasMyNetAppRuntime? _runtime;
    private bool _isTrusted;

    public AtlasMyNetAppItemViewModel(
        AtlasMyNetAppDefinition definition,
        AtlasMyNetAppStore store)
    {
        Definition = definition;
        _store = store;
        _isTrusted = store.IsTrusted(definition);
    }

    public AtlasMyNetAppDefinition Definition { get; }
    public string DisplayName => Definition.DisplayName;
    public string UrlText => Definition.IsNative
        ? "Desktop app"
        : Runtime?.Url ?? Definition.UrlTemplate;
    public string CommandTrustText => AtlasMyNetAppStore.HasCommands(Definition)
        ? IsTrusted ? "Commands trusted" : "Review required"
        : "URL only";
    public bool HasCommands => AtlasMyNetAppStore.HasCommands(Definition);
    public bool NeedsTrust => HasCommands && !IsTrusted;
    public bool IsRunning => Runtime is not null;
    public bool CanStart => Definition.Launch is not null
                            && !IsRunning
                            && Status != AtlasMyNetAppStatus.Starting
                            && Status != AtlasMyNetAppStatus.Missing
                            && IsTrusted;
    public bool CanOpen => Runtime is not null
                           || Definition.Launch is null
                           || CanStart;
    public bool CanStop => Runtime is not null;
    public bool CanRemove => !string.Equals(
        Definition.Id,
        AtlasMyNetAppStore.PingLabAppId,
        StringComparison.OrdinalIgnoreCase)
        && !string.Equals(Definition.Id, HyperlibMyNetRegistration.AppId, StringComparison.OrdinalIgnoreCase);

    public AtlasMyNetAppStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanOpen));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
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

    public AtlasMyNetAppRuntime? Runtime
    {
        get => _runtime;
        private set
        {
            if (SetProperty(ref _runtime, value))
            {
                OnPropertyChanged(nameof(UrlText));
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanOpen));
                OnPropertyChanged(nameof(CanStop));
            }
        }
    }

    public bool IsTrusted
    {
        get => _isTrusted;
        private set
        {
            if (SetProperty(ref _isTrusted, value))
            {
                OnPropertyChanged(nameof(CommandTrustText));
                OnPropertyChanged(nameof(NeedsTrust));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanOpen));
            }
        }
    }

    public void SetStatus(
        AtlasMyNetAppStatus status,
        string text,
        AtlasMyNetAppRuntime? runtime)
    {
        Status = status;
        StatusText = text;
        Runtime = runtime;
        ErrorMessage = string.Empty;
    }

    public void SetError(
        string message,
        AtlasMyNetAppRuntime? runtime = null)
    {
        Status = AtlasMyNetAppStatus.Error;
        StatusText = "Error";
        Runtime = runtime;
        ErrorMessage = message;
    }

    public void RefreshTrust() => IsTrusted = _store.IsTrusted(Definition);
}
