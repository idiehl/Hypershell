using System.Net.Sockets;

namespace Hypershell.Models;

public sealed class AtlasMyNetAppDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<AtlasMyNetAppDefinition> Apps { get; set; } = [];
}

public sealed class AtlasMyNetAppDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "Local app";
    public string IconPath { get; set; } = string.Empty;
    public bool IsNative { get; set; }
    public string UrlTemplate { get; set; } = "http://127.0.0.1:{port}/";
    public List<int> Ports { get; set; } = [];
    public AtlasMyNetLaunchDefinition? Launch { get; set; }
    public AtlasMyNetHealthCheckDefinition? HealthCheck { get; set; }
    public AtlasMyNetStopDefinition? Stop { get; set; }
    public bool AllowExternalResources { get; set; }
    public bool ShowNavigationToolbar { get; set; } = true;

    public AtlasMyNetAppDefinition Clone() =>
        new()
        {
            Id = Id,
            DisplayName = DisplayName,
            IconPath = IconPath,
            IsNative = IsNative,
            UrlTemplate = UrlTemplate,
            Ports = Ports is null ? [] : [.. Ports],
            Launch = Launch?.Clone(),
            HealthCheck = HealthCheck?.Clone(),
            Stop = Stop?.Clone(),
            AllowExternalResources = AllowExternalResources,
            ShowNavigationToolbar = ShowNavigationToolbar
        };
}

public sealed class AtlasMyNetLaunchDefinition
{
    public string Executable { get; set; } = string.Empty;
    public List<string> Arguments { get; set; } = [];
    public string WorkingDirectory { get; set; } = string.Empty;
    public Dictionary<string, string> Environment { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public AtlasMyNetLaunchDefinition Clone() =>
        new()
        {
            Executable = Executable,
            Arguments = Arguments is null ? [] : [.. Arguments],
            WorkingDirectory = WorkingDirectory,
            Environment = Environment is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(
                    Environment,
                    StringComparer.OrdinalIgnoreCase)
        };
}

public sealed class AtlasMyNetHealthCheckDefinition
{
    public string Path { get; set; } = "/";
    public int ExpectedStatusCode { get; set; } = 200;
    public string HeaderName { get; set; } = string.Empty;
    public string HeaderValue { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;

    public AtlasMyNetHealthCheckDefinition Clone() =>
        new()
        {
            Path = Path,
            ExpectedStatusCode = ExpectedStatusCode,
            HeaderName = HeaderName,
            HeaderValue = HeaderValue,
            TimeoutSeconds = TimeoutSeconds
        };
}

public enum AtlasMyNetStopMode
{
    ManagedProcess,
    Command,
    None
}

public sealed class AtlasMyNetStopDefinition
{
    public AtlasMyNetStopMode Mode { get; set; } =
        AtlasMyNetStopMode.ManagedProcess;
    public string Executable { get; set; } = string.Empty;
    public List<string> Arguments { get; set; } = [];
    public string WorkingDirectory { get; set; } = string.Empty;

    public AtlasMyNetStopDefinition Clone() =>
        new()
        {
            Mode = Mode,
            Executable = Executable,
            Arguments = Arguments is null ? [] : [.. Arguments],
            WorkingDirectory = WorkingDirectory
        };
}

public enum AtlasMyNetAppStatus
{
    Missing,
    Stopped,
    Starting,
    Running,
    Error
}

public sealed record AtlasMyNetAppRuntime(
    string AppId,
    int ProcessId,
    string ExecutablePath,
    DateTimeOffset StartedAtUtc,
    int Port,
    string Url,
    bool StartedThisSession);

public enum NetworkTransport
{
    Tcp,
    Udp
}

public sealed record NetworkSocketSnapshot(
    string Key,
    NetworkTransport Transport,
    AddressFamily AddressFamily,
    string State,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    int RemotePort,
    int ProcessId,
    string ProcessName,
    string ExecutablePath,
    string OwnerName,
    string OwnerSid,
    bool IsCurrentUserOwned,
    DateTimeOffset? ProcessStartTimeUtc)
{
    public bool IsListener =>
        Transport == NetworkTransport.Tcp
        && string.Equals(State, "Listen", StringComparison.OrdinalIgnoreCase);

    public string ProtocolText =>
        $"{Transport.ToString().ToUpperInvariant()} "
        + (AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4");
    public string ProcessSummary =>
        $"{ProcessName} · PID {ProcessId}"
        + (string.IsNullOrWhiteSpace(OwnerName) ? string.Empty : $" · {OwnerName}");

    public bool CanRequestProcessStop =>
        ProcessId > 4
        && ProcessId != Environment.ProcessId
        && IsCurrentUserOwned
        && ProcessStartTimeUtc.HasValue
        && !string.IsNullOrWhiteSpace(ExecutablePath);

    public string LocalEndpoint => FormatEndpoint(LocalAddress, LocalPort);
    public string RemoteEndpoint => RemotePort <= 0
        ? "—"
        : FormatEndpoint(RemoteAddress, RemotePort);

    public static string FormatEndpoint(string address, int port) =>
        address.Contains(':', StringComparison.Ordinal)
            ? $"[{address}]:{port}"
            : $"{address}:{port}";
}

public sealed record NetworkProcessIdentity(
    int ProcessId,
    string Name,
    string ExecutablePath,
    string OwnerName,
    string OwnerSid,
    bool IsCurrentUserOwned,
    DateTimeOffset? StartTimeUtc);

public sealed record AtlasMyNetDiscoveredWebApp(
    string Key,
    string DisplayName,
    string Url,
    int ProcessId,
    string ProcessName,
    int StatusCode,
    DateTimeOffset DetectedAtUtc);

public sealed record BrowserNavigationPolicy(
    string AppId,
    string DisplayName,
    string HomeAddress,
    bool AllowExternalResources);

public sealed record AtlasMyNetOpenAppRequest(
    AtlasMyNetAppDefinition Definition,
    string Url,
    string? TrustedHyperlibRoot = null);

public enum AtlasMyNetStopOutcome
{
    Stopped,
    AlreadyStopped,
    NeedsForce,
    Rejected,
    Failed
}

public sealed record AtlasMyNetStopResult(
    AtlasMyNetStopOutcome Outcome,
    string Message,
    int ProcessId = 0);

public enum NetworkPayloadFormat
{
    Text,
    Hex
}

public enum NetworkLineEnding
{
    None,
    Lf,
    CrLf
}

public enum NetworkTextEncodingKind
{
    Utf8,
    Ascii,
    Utf16LittleEndian,
    Latin1
}

public sealed record NetworkClientLaunchRequest(
    NetworkTransport Transport,
    string Host,
    int Port,
    bool AutoConnect,
    string ContextDescription = "");

public sealed record NetworkTranscriptEntry(
    DateTimeOffset Timestamp,
    string Direction,
    string Text,
    int ByteCount)
{
    public string TimestampText => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
}
