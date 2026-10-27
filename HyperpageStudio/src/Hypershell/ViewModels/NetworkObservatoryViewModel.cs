using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Threading;

namespace Hypershell.ViewModels;

public sealed class NetworkObservatoryViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan DiscoveryCacheDuration =
        TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultRefreshTimeout =
        TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultNetworkChangeDebounce =
        TimeSpan.FromSeconds(2);

    private readonly IWindowsNetworkObservationService _observationService;
    private readonly ILocalNetworkDiscoveryService _discoveryService;
    private readonly INetworkExposureAnalysisService _exposureService;
    private readonly INetworkCensusService _censusService;
    private readonly INetworkSocketInventoryService _socketInventory;
    private readonly NetworkDeviceCorrelationService _correlationService;
    private readonly NetworkObservatoryStateStore _stateStore;
    private readonly TimeSpan _refreshTimeout;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _networkChangeDebounceTimer;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private IReadOnlyList<NetworkDiscoveryObservation> _cachedObservations = [];
    private IReadOnlyList<NetworkCensusHostSnapshot> _censusResults = [];
    private NetworkOverviewSnapshot? _latestOverview;
    private NetworkObservatoryStateDocument? _state;
    private CancellationTokenSource? _censusCancellation;
    private DateTimeOffset _lastDiscoveryUtc;
    private bool _isActive;
    private bool _isRefreshing;
    private bool _networkEventsAttached;
    private bool _refreshPending;
    private bool _forceDiscoveryPending;
    private bool _isCensusAuthorized;
    private bool _isCensusRunning;
    private NetworkCensusSubnetChoice? _selectedCensusSubnet;
    private string _censusPortsText =
        "22, 53, 80, 443, 445, 3389, 50080, 50443";
    private string _censusProgressText = "No census running.";
    private string _censusStatusText =
        "Select a connected subnet and confirm authorization before scanning.";
    private string _hostSummary = "Waiting for network observation…";
    private string _networkProfileSummary = "Profiles unavailable";
    private string _firewallSummary = "Firewall state unavailable";
    private string _networkSummary = "No network snapshot yet.";
    private string _nearbySummary = "No nearby-device snapshot yet.";
    private string _exposureSummary = "No listener analysis yet.";
    private string _changeSummary = "No baseline loaded.";
    private string _statusMessage = "Network Observatory is ready.";
    private string _lastUpdatedText = "Not yet updated";
    private string _discoveryDiagnostics = string.Empty;
    private bool _isDisposed;

    public NetworkObservatoryViewModel(
        IWindowsNetworkObservationService observationService,
        ILocalNetworkDiscoveryService discoveryService,
        INetworkExposureAnalysisService exposureService,
        INetworkCensusService censusService,
        INetworkSocketInventoryService socketInventory,
        NetworkDeviceCorrelationService correlationService,
        NetworkObservatoryStateStore stateStore,
        TimeSpan? refreshTimeout = null,
        TimeSpan? networkChangeDebounce = null)
    {
        _observationService = observationService;
        _discoveryService = discoveryService;
        _exposureService = exposureService;
        _censusService = censusService;
        _socketInventory = socketInventory;
        _correlationService = correlationService;
        _stateStore = stateStore;
        _refreshTimeout = refreshTimeout ?? DefaultRefreshTimeout;
        if (_refreshTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshTimeout),
                "The refresh timeout must be greater than zero.");
        }

        var debounceInterval = networkChangeDebounce
                               ?? DefaultNetworkChangeDebounce;
        if (debounceInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(networkChangeDebounce),
                "The network-change debounce interval must be greater than zero.");
        }

        _state = _stateStore.Load();
        if (_state is not null)
        {
            ReplaceCollection(Changes, _state.History);
            ChangeSummary =
                $"{Changes.Count} saved event{(Changes.Count == 1 ? string.Empty : "s")}";
        }

        RefreshCommand = new AsyncRelayCommand(
            () => RefreshAsync(forceDiscovery: true),
            () => !IsRefreshing);
        StartCensusCommand = new AsyncRelayCommand(
            StartCensusAsync,
            () => CanStartCensus);
        CancelCensusCommand = new RelayCommand(
            CancelCensus,
            () => IsCensusRunning);
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        _refreshTimer.Tick += OnRefreshTimerTick;
        _networkChangeDebounceTimer = new DispatcherTimer
        {
            Interval = debounceInterval
        };
        _networkChangeDebounceTimer.Tick += OnNetworkChangeDebounceTick;
    }

    public ObservableCollection<NetworkInterfaceSnapshot> Interfaces { get; } = [];
    public ObservableCollection<NetworkRouteSnapshot> Routes { get; } = [];
    public ObservableCollection<NetworkFirewallProfileSnapshot>
        FirewallProfiles
    { get; } = [];
    public ObservableCollection<NearbyNetworkDeviceSnapshot> NearbyDevices { get; } = [];
    public ObservableCollection<NetworkExposureSnapshot> Exposures { get; } = [];
    public ObservableCollection<NetworkServiceCandidateSnapshot>
        ServiceCandidates
    { get; } = [];
    public ObservableCollection<NetworkCensusSubnetChoice> CensusSubnets { get; } = [];
    public ObservableCollection<NetworkCensusHostSnapshot> CensusHosts { get; } = [];
    public ObservableCollection<NetworkChangeSnapshot> Changes { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand StartCensusCommand { get; }
    public RelayCommand CancelCensusCommand { get; }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public NetworkCensusSubnetChoice? SelectedCensusSubnet
    {
        get => _selectedCensusSubnet;
        set
        {
            if (SetProperty(ref _selectedCensusSubnet, value))
            {
                OnPropertyChanged(nameof(CanStartCensus));
                StartCensusCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CensusPortsText
    {
        get => _censusPortsText;
        set
        {
            if (SetProperty(ref _censusPortsText, value))
            {
                StartCensusCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsCensusAuthorized
    {
        get => _isCensusAuthorized;
        set
        {
            if (SetProperty(ref _isCensusAuthorized, value))
            {
                OnPropertyChanged(nameof(CanStartCensus));
                StartCensusCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsCensusRunning
    {
        get => _isCensusRunning;
        private set
        {
            if (SetProperty(ref _isCensusRunning, value))
            {
                OnPropertyChanged(nameof(CanStartCensus));
                StartCensusCommand.RaiseCanExecuteChanged();
                CancelCensusCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanStartCensus =>
        !IsCensusRunning
        && IsCensusAuthorized
        && SelectedCensusSubnet is not null;

    public string CensusProgressText
    {
        get => _censusProgressText;
        private set => SetProperty(ref _censusProgressText, value);
    }

    public string CensusStatusText
    {
        get => _censusStatusText;
        private set => SetProperty(ref _censusStatusText, value);
    }

    public string HostSummary
    {
        get => _hostSummary;
        private set => SetProperty(ref _hostSummary, value);
    }

    public string NetworkProfileSummary
    {
        get => _networkProfileSummary;
        private set => SetProperty(ref _networkProfileSummary, value);
    }

    public string FirewallSummary
    {
        get => _firewallSummary;
        private set => SetProperty(ref _firewallSummary, value);
    }

    public string NetworkSummary
    {
        get => _networkSummary;
        private set => SetProperty(ref _networkSummary, value);
    }

    public string NearbySummary
    {
        get => _nearbySummary;
        private set => SetProperty(ref _nearbySummary, value);
    }

    public string ExposureSummary
    {
        get => _exposureSummary;
        private set => SetProperty(ref _exposureSummary, value);
    }

    public string ChangeSummary
    {
        get => _changeSummary;
        private set => SetProperty(ref _changeSummary, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => SetProperty(ref _lastUpdatedText, value);
    }

    public string DiscoveryDiagnostics
    {
        get => _discoveryDiagnostics;
        private set => SetProperty(ref _discoveryDiagnostics, value);
    }

    public string RouterBoundaryText =>
        "Router administration, WAN configuration and authenticated router telemetry are intentionally excluded. Router/NAT exposure remains unknown.";

    public string DataSourceText =>
        "Local, read-only Windows network APIs · no Atlas server connection required";

    public void SetActive(bool active)
    {
        if (_isDisposed || _isActive == active)
        {
            return;
        }

        _isActive = active;
        if (active)
        {
            AttachNetworkChangeEvents();
            _refreshTimer.Start();
            _ = RefreshAsync(forceDiscovery: _lastDiscoveryUtc == default);
        }
        else
        {
            _refreshTimer.Stop();
            _networkChangeDebounceTimer.Stop();
            DetachNetworkChangeEvents();
        }
    }

    public async Task RefreshAsync(bool forceDiscovery)
    {
        if (_isDisposed)
        {
            return;
        }

        if (!await _refreshGate.WaitAsync(0))
        {
            _refreshPending = true;
            _forceDiscoveryPending |= forceDiscovery;
            return;
        }

        IsRefreshing = true;
        try
        {
            var nextForceDiscovery = forceDiscovery;
            do
            {
                nextForceDiscovery |= _forceDiscoveryPending;
                _forceDiscoveryPending = false;
                _refreshPending = false;
                await RefreshOnceAsync(nextForceDiscovery);
                nextForceDiscovery = false;
            }
            while (_refreshPending && _isActive && !_isDisposed);
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
        }
    }

    private async Task RefreshOnceAsync(bool forceDiscovery)
    {
        var stopwatch = Stopwatch.StartNew();
        using var refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _disposeCancellation.Token);
        refreshCancellation.CancelAfter(_refreshTimeout);
        var cancellationToken = refreshCancellation.Token;
        StatusMessage = "Reading Windows interfaces, routes and firewall state…";
        try
        {
            var overviewTask = _observationService.CaptureAsync(cancellationToken);
            var socketsTask = Task.Run(_socketInventory.Capture, cancellationToken);
            await Task.WhenAll(overviewTask, socketsTask).WaitAsync(cancellationToken);
            var overview = await overviewTask;
            var sockets = await socketsTask;
            _latestOverview = overview;
            ApplyOverviewSnapshot(overview);
            DiscoveryDiagnostics = overview.Diagnostics;
            LastUpdatedText = $"Core state updated {DateTimeOffset.Now:HH:mm:ss}";

            var shouldDiscover = forceDiscovery
                                 || _lastDiscoveryUtc == default
                                 || DateTimeOffset.UtcNow - _lastDiscoveryUtc
                                 >= DiscoveryCacheDuration;
            if (shouldDiscover)
            {
                StatusMessage =
                    "Core state ready; checking mDNS, SSDP and WS-Discovery…";
                var discovery = await _discoveryService.DiscoverAsync(
                        overview,
                        cancellationToken)
                    .WaitAsync(cancellationToken);
                _cachedObservations = discovery.Observations;
                _lastDiscoveryUtc = DateTimeOffset.UtcNow;
                DiscoveryDiagnostics = JoinDiagnostics(
                    overview.Diagnostics,
                    discovery.Diagnostics);
            }

            StatusMessage = "Analyzing local listeners and nearby devices…";
            var exposureTask = _exposureService.AnalyzeAsync(
                overview,
                sockets,
                cancellationToken);
            var devicesTask = _correlationService.CorrelateAsync(
                overview,
                _cachedObservations,
                _censusResults,
                cancellationToken);
            var firstAnalysis = await Task.WhenAny(exposureTask, devicesTask)
                .WaitAsync(cancellationToken);
            StatusMessage = ReferenceEquals(firstAnalysis, exposureTask)
                ? "Listener analysis ready; correlating nearby devices…"
                : "Nearby-device correlation ready; inspecting local listeners…";
            await Task.WhenAll(exposureTask, devicesTask).WaitAsync(cancellationToken);
            var exposures = await exposureTask;
            var devices = await devicesTask;
            ApplyAnalysisSnapshot(devices, exposures);
            CompareAndPersistState(overview, devices, exposures);
            stopwatch.Stop();
            LastUpdatedText = $"Updated {DateTimeOffset.Now:HH:mm:ss}";
            StatusMessage = shouldDiscover
                ? $"Network state and advertised services updated in {stopwatch.Elapsed.TotalSeconds:0.0} s."
                : $"Network state updated in {stopwatch.Elapsed.TotalSeconds:0.0} s; discovery cache retained.";
        }
        catch (OperationCanceledException) when (_isDisposed)
        {
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            var interruptedStage = StatusMessage.Trim().TrimEnd('…', '.');
            StatusMessage =
                $"Refresh exceeded {_refreshTimeout.TotalSeconds:0} s while {interruptedStage.ToLowerInvariant()}. The last completed data was retained; Hypershell will retry automatically.";
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            DiscoveryDiagnostics = JoinDiagnostics(
                DiscoveryDiagnostics,
                $"{exception.GetType().Name}: {exception.Message}");
            StatusMessage =
                "Network refresh was interrupted. The last completed data was retained; Hypershell will retry automatically.";
        }
    }

    private void ApplyOverviewSnapshot(NetworkOverviewSnapshot overview)
    {
        ReplaceCollection(Interfaces, overview.Interfaces);
        ReplaceCollection(Routes, overview.Routes);
        ReplaceCollection(FirewallProfiles, overview.FirewallProfiles);
        RefreshCensusSubnetChoices(overview);

        var physical = overview.Interfaces.Count(item =>
            item.Status == OperationalStatus.Up && !item.IsVirtual);
        var virtualCount = overview.Interfaces.Count(item =>
            item.Status == OperationalStatus.Up && item.IsVirtual);
        HostSummary = string.IsNullOrWhiteSpace(overview.DomainName)
            ? overview.HostName
            : $"{overview.HostName} · {overview.DomainName}";
        NetworkProfileSummary = string.Join(
            " · ",
            overview.Interfaces
                .Where(item => item.Status == OperationalStatus.Up
                               && !string.IsNullOrWhiteSpace(item.NetworkCategory))
                .Select(item => $"{item.Name}: {item.NetworkCategory}")
                .Distinct(StringComparer.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(NetworkProfileSummary))
        {
            NetworkProfileSummary = "Active profile unavailable";
        }

        FirewallSummary = overview.FirewallProfiles.Count == 0
            ? "Firewall metadata unavailable"
            : string.Join(" · ", overview.FirewallProfiles.Select(item => item.Summary));
        NetworkSummary =
            $"{physical} physical · {virtualCount} virtual/VPN · "
            + $"{overview.Routes.Count} routes · {overview.Neighbors.Count} neighbor entries";
    }

    private void ApplyAnalysisSnapshot(
        IReadOnlyList<NearbyNetworkDeviceSnapshot> devices,
        IReadOnlyList<NetworkExposureSnapshot> exposures)
    {
        ReplaceCollection(NearbyDevices, devices);
        ReplaceCollection(Exposures, exposures);
        ReplaceCollection(
            ServiceCandidates,
            NetworkExposureAnalysisService.CreateServiceCandidates(exposures));
        NearbySummary =
            $"{devices.Count} correlated device{(devices.Count == 1 ? string.Empty : "s")} · "
            + $"{_cachedObservations.Count} advertised service observation{(_cachedObservations.Count == 1 ? string.Empty : "s")}";
        var reachable = exposures.Count(item => item.Classification.StartsWith(
            "LAN reachable",
            StringComparison.OrdinalIgnoreCase));
        var loopback = exposures.Count(item =>
            item.BindScope == NetworkBindScope.LoopbackOnly);
        ExposureSummary =
            $"{exposures.Count} listening endpoints · {reachable} LAN reachable · "
            + $"{loopback} loopback only";
    }

    private void RefreshCensusSubnetChoices(NetworkOverviewSnapshot overview)
    {
        var choices = new List<NetworkCensusSubnetChoice>();
        foreach (var item in overview.Interfaces.Where(item =>
                     item.Status == OperationalStatus.Up && !item.IsVirtual))
        {
            foreach (var addressText in item.Addresses)
            {
                var parts = addressText.Split('/');
                if (parts.Length != 2
                    || !IPAddress.TryParse(parts[0], out var address)
                    || address.AddressFamily != AddressFamily.InterNetwork
                    || !int.TryParse(parts[1], out var prefixLength)
                    || address.Equals(IPAddress.Any)
                    || IPAddress.IsLoopback(address))
                {
                    continue;
                }

                choices.Add(NetworkCidrUtility.CreateCensusChoice(
                    address,
                    prefixLength,
                    item.Name));
            }
        }

        var selectedCidr = SelectedCensusSubnet?.Cidr;
        ReplaceCollection(
            CensusSubnets,
            choices
                .GroupBy(item => item.Cidr, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.Cidr, StringComparer.OrdinalIgnoreCase));
        SelectedCensusSubnet = CensusSubnets.FirstOrDefault(item => string.Equals(
                                    item.Cidr,
                                    selectedCidr,
                                    StringComparison.OrdinalIgnoreCase))
                                ?? CensusSubnets.FirstOrDefault();
    }

    private void CompareAndPersistState(
        NetworkOverviewSnapshot overview,
        IReadOnlyList<NearbyNetworkDeviceSnapshot> devices,
        IReadOnlyList<NetworkExposureSnapshot> exposures)
    {
        var comparison = NetworkObservatoryStateStore.Compare(
            _state,
            overview,
            devices,
            exposures);
        _state = comparison.State;
        ReplaceCollection(Changes, comparison.State.History);
        ChangeSummary = comparison.BaselineCreated
            ? "Baseline created · future changes will be recorded"
            : comparison.NewChanges.Count == 0
                ? $"No changes · {Changes.Count} saved event{(Changes.Count == 1 ? string.Empty : "s")}"
                : $"{comparison.NewChanges.Count} new · {Changes.Count} saved";
        try
        {
            _stateStore.Save(comparison.State);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or InvalidOperationException)
        {
            StatusMessage = $"Snapshot updated, but history could not be saved: {exception.Message}";
        }
    }

    private async Task StartCensusAsync()
    {
        if (!CanStartCensus
            || SelectedCensusSubnet is null
            || _latestOverview is null)
        {
            CensusStatusText =
                "Refresh the panel, select a connected subnet and confirm authorization.";
            return;
        }

        IReadOnlyList<int> ports;
        try
        {
            ports = NetworkCensusService.ParsePorts(CensusPortsText);
        }
        catch (FormatException exception)
        {
            CensusStatusText = exception.Message;
            return;
        }

        if (!CensusSubnets.Any(item => string.Equals(
                item.Cidr,
                SelectedCensusSubnet.Cidr,
                StringComparison.OrdinalIgnoreCase)))
        {
            CensusStatusText = "The selected subnet is no longer attached to this PC.";
            return;
        }

        IsCensusAuthorized = false;
        IsCensusRunning = true;
        CensusHosts.Clear();
        CensusStatusText =
            $"Scanning {SelectedCensusSubnet.Cidr} with {ports.Count} selected TCP ports…";
        CensusProgressText = "Starting bounded census…";
        _censusCancellation?.Dispose();
        _censusCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _disposeCancellation.Token);
        var progress = new Progress<NetworkCensusProgress>(update =>
        {
            CensusProgressText =
                $"{update.Percent}% · {update.Completed}/{update.Total} · {update.CurrentAddress}";
        });
        try
        {
            _censusResults = await _censusService.ScanAsync(
                SelectedCensusSubnet.Cidr,
                ports,
                _latestOverview.Neighbors,
                progress,
                _censusCancellation.Token);
            ReplaceCollection(CensusHosts, _censusResults);
            CensusStatusText =
                $"Census complete · {_censusResults.Count} responding host{(_censusResults.Count == 1 ? string.Empty : "s")}.";
            CensusProgressText = "100% · complete";
        }
        catch (OperationCanceledException)
        {
            CensusStatusText = "Census cancelled. Partial results were discarded.";
            CensusProgressText = "Cancelled";
            _censusResults = [];
            CensusHosts.Clear();
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or InvalidOperationException
                                           or SocketException
                                           or PingException)
        {
            CensusStatusText = $"Census failed: {exception.Message}";
        }
        finally
        {
            IsCensusRunning = false;
            _censusCancellation?.Dispose();
            _censusCancellation = null;
        }

        await RefreshAsync(forceDiscovery: false);
    }

    private void CancelCensus() => _censusCancellation?.Cancel();

    private async void OnRefreshTimerTick(object? sender, EventArgs eventArgs) =>
        await RefreshAsync(forceDiscovery: false);

    private void AttachNetworkChangeEvents()
    {
        if (_networkEventsAttached)
        {
            return;
        }

        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        _networkEventsAttached = true;
    }

    private void DetachNetworkChangeEvents()
    {
        if (!_networkEventsAttached)
        {
            return;
        }

        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _networkEventsAttached = false;
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs eventArgs) =>
        QueueNetworkChangeRefresh();

    private void OnNetworkAvailabilityChanged(
        object? sender,
        NetworkAvailabilityEventArgs eventArgs) => QueueNetworkChangeRefresh();

    private void QueueNetworkChangeRefresh()
    {
        if (_isDisposed || !_isActive)
        {
            return;
        }

        var dispatcher = _refreshTimer.Dispatcher;
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_isDisposed || !_isActive)
            {
                return;
            }

            StatusMessage =
                "Windows reported a network change; waiting briefly for adapter state to settle…";
            _networkChangeDebounceTimer.Stop();
            _networkChangeDebounceTimer.Start();
        }));
    }

    private async void OnNetworkChangeDebounceTick(
        object? sender,
        EventArgs eventArgs)
    {
        _networkChangeDebounceTimer.Stop();
        await RefreshAsync(forceDiscovery: true);
    }

    private static string JoinDiagnostics(params string[] values) =>
        string.Join(
            " · ",
            values.Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase));

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

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= OnRefreshTimerTick;
        _networkChangeDebounceTimer.Stop();
        _networkChangeDebounceTimer.Tick -= OnNetworkChangeDebounceTick;
        DetachNetworkChangeEvents();
        _disposeCancellation.Cancel();
        _censusCancellation?.Cancel();
        _censusCancellation?.Dispose();
    }
}
