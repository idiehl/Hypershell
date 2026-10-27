using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Hypershell.Models;

namespace Hypershell.Services;

public enum VisualPageBuildOutcome
{
    Succeeded,
    Failed,
    Cancelled,
    SdkUnavailable
}

public sealed class VisualPageBuildResult
{
    public required VisualPageBuildOutcome Outcome { get; init; }
    public string SnapshotPath { get; init; } = string.Empty;
    public string Output { get; init; } = string.Empty;
    public IReadOnlyList<VisualPageDiagnostic> Diagnostics { get; init; } = [];
    public string Revision { get; init; } = string.Empty;
}

public sealed partial class VisualPagePreviewBuildService : IDisposable
{
    private const int MaximumSnapshotFiles = 5_000;
    private const long MaximumSnapshotBytes = 256L * 1024 * 1024;
    private readonly BoundedProcessRunner _runner;
    private readonly VisualPageGenerator _generator;
    private readonly string _cacheRoot;
    private readonly object _sync = new();
    private CancellationTokenSource? _latestBuild;
    private long _buildSequence;

    public VisualPagePreviewBuildService(
        BoundedProcessRunner runner,
        VisualPageGenerator generator,
        string? cacheRoot = null)
    {
        _runner = runner;
        _generator = generator;
        _cacheRoot = Path.GetFullPath(cacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hypershell",
            "VisualPageStudio",
            "preview"));
    }

    public async Task<VisualPageBuildResult> BuildLatestAsync(
        VisualPageProjectSession session,
        IReadOnlyDictionary<string, string>? sourceOverrides = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        CancellationTokenSource source;
        long sequence;
        lock (_sync)
        {
            _latestBuild?.Cancel();
            _latestBuild?.Dispose();
            _latestBuild = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            source = _latestBuild;
            sequence = ++_buildSequence;
        }

        try
        {
            var dotnet = ResolveDotNetExecutable();
            if (dotnet is null)
            {
                return new VisualPageBuildResult
                {
                    Outcome = VisualPageBuildOutcome.SdkUnavailable,
                    Output = ".NET 10 SDK is required for compiled preview.",
                    Revision = session.Page.Revision
                };
            }

            var snapshot = await PrepareSnapshotAsync(
                session,
                sequence,
                sourceOverrides,
                source.Token);
            var result = await _runner.RunAsync(new BoundedProcessRequest
            {
                ExecutablePath = dotnet,
                Arguments =
                [
                    "build",
                    Path.Combine(snapshot, "src", "Host", "Host.csproj"),
                    "--configuration",
                    "Debug",
                    "--nologo",
                    "--verbosity",
                    "minimal"
                ],
                WorkingDirectory = snapshot,
                Timeout = TimeSpan.FromMinutes(3),
                MaximumStandardOutputCharacters = 2 * 1024 * 1024,
                MaximumStandardErrorCharacters = 2 * 1024 * 1024
            }, source.Token);
            if (source.IsCancellationRequested
                || sequence != Interlocked.Read(ref _buildSequence))
            {
                return new VisualPageBuildResult
                {
                    Outcome = VisualPageBuildOutcome.Cancelled,
                    Output = "A newer preview build replaced this build.",
                    Revision = session.Page.Revision
                };
            }
            var output = string.Join(
                Environment.NewLine,
                new[] { result.StandardOutput, result.StandardError, result.FailureMessage }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            return new VisualPageBuildResult
            {
                Outcome = result.ExitCode == 0
                    ? VisualPageBuildOutcome.Succeeded
                    : VisualPageBuildOutcome.Failed,
                SnapshotPath = snapshot,
                Output = output,
                Diagnostics = ParseDiagnostics(
                    output,
                    session.Page.SourceMappings),
                Revision = session.Page.Revision
            };
        }
        catch (OperationCanceledException)
        {
            return new VisualPageBuildResult
            {
                Outcome = VisualPageBuildOutcome.Cancelled,
                Output = "A newer preview build replaced this build.",
                Revision = session.Page.Revision
            };
        }
    }

    public void Cancel()
    {
        lock (_sync)
        {
            _latestBuild?.Cancel();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _latestBuild?.Cancel();
            _latestBuild?.Dispose();
            _latestBuild = null;
        }
    }

    private async Task<string> PrepareSnapshotAsync(
        VisualPageProjectSession session,
        long sequence,
        IReadOnlyDictionary<string, string>? sourceOverrides,
        CancellationToken cancellationToken)
    {
        var generation = _generator.Generate(session.Project, session.Page);
        if (!Guid.TryParseExact(session.Project.ProjectId, "N", out _))
        {
            throw new VisualPageValidationException(
                "The preview project identity is invalid.");
        }
        Directory.CreateDirectory(_cacheRoot);
        var projectRoot = VisualPageProjectService.ResolveProjectPath(
            _cacheRoot,
            session.Project.ProjectId);
        Directory.CreateDirectory(projectRoot);
        var snapshot = VisualPageProjectService.ResolveProjectPath(
            projectRoot,
            $"{sequence:D8}-{session.Page.Revision[..Math.Min(12, session.Page.Revision.Length)]}");
        if (Directory.Exists(snapshot))
        {
            ValidateCacheDescendant(snapshot);
            Directory.Delete(snapshot, recursive: true);
        }
        Directory.CreateDirectory(snapshot);

        var counters = new SnapshotCounters();
        foreach (var directory in new[] { "src", "Shared", "Assets" })
        {
            var source = VisualPageProjectService.ResolveProjectPath(
                session.RootPath,
                directory);
            if (Directory.Exists(source))
            {
                await CopyDirectoryAsync(
                    source,
                    Path.Combine(snapshot, directory),
                    counters,
                    cancellationToken);
            }
        }

        foreach (var artifact in generation.Artifacts)
        {
            await WriteSnapshotTextAsync(
                snapshot,
                artifact.RelativePath,
                artifact.Content,
                cancellationToken);
        }
        if (sourceOverrides is not null)
        {
            foreach (var sourceOverride in sourceOverrides)
            {
                await WriteSnapshotTextAsync(
                    snapshot,
                    sourceOverride.Key,
                    sourceOverride.Value,
                    cancellationToken);
            }
        }
        await WriteSnapshotTextAsync(
            snapshot,
            VisualPageProjectService.ProjectManifestFileName,
            System.Text.Json.JsonSerializer.Serialize(
                session.Project,
                VisualPageJson.Options),
            cancellationToken);
        await WriteSnapshotTextAsync(
            snapshot,
            session.Project.Pages.First(page => page.PageId == session.Page.PageId).DocumentPath,
            System.Text.Json.JsonSerializer.Serialize(
                session.Page,
                VisualPageJson.Options),
            cancellationToken);
        PruneOldSnapshots(projectRoot, keep: 4);
        return snapshot;
    }

    private static async Task CopyDirectoryAsync(
        string source,
        string destination,
        SnapshotCounters counters,
        CancellationToken cancellationToken)
    {
        VisualPageProjectService.RejectReparsePoint(source);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            VisualPageProjectService.RejectReparsePoint(file);
            var info = new FileInfo(file);
            counters.Files++;
            counters.Bytes += info.Length;
            if (counters.Files > MaximumSnapshotFiles
                || counters.Bytes > MaximumSnapshotBytes)
            {
                throw new VisualPageValidationException(
                    "The preview source exceeds the bounded snapshot limits.");
            }
            var target = Path.Combine(destination, info.Name);
            await using var input = info.OpenRead();
            await using var output = new FileStream(
                target,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous);
            await input.CopyToAsync(output, cancellationToken);
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || name.Equals("runtime", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            VisualPageProjectService.RejectReparsePoint(directory);
            await CopyDirectoryAsync(
                directory,
                Path.Combine(destination, name),
                counters,
                cancellationToken);
        }
    }

    private static async Task WriteSnapshotTextAsync(
        string snapshot,
        string relativePath,
        string content,
        CancellationToken cancellationToken)
    {
        var path = VisualPageProjectService.ResolveProjectPath(
            snapshot,
            relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            content,
            new UTF8Encoding(false),
            cancellationToken);
    }

    private void ValidateCacheDescendant(string candidate)
    {
        var root = _cacheRoot.TrimEnd(Path.DirectorySeparatorChar) +
                   Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(candidate);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Preview cleanup target escaped the Hyperpage Studio cache.");
        }
    }

    private void PruneOldSnapshots(string projectRoot, int keep)
    {
        foreach (var directory in Directory.EnumerateDirectories(projectRoot)
                     .OrderByDescending(Directory.GetCreationTimeUtc)
                     .Skip(keep))
        {
            ValidateCacheDescendant(directory);
            VisualPageProjectService.RejectReparsePoint(directory);
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static string? ResolveDotNetExecutable()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet",
                "dotnet.exe")
        };
        return candidates.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate)
            && Path.IsPathRooted(candidate)
            && File.Exists(candidate));
    }

    private static IReadOnlyList<VisualPageDiagnostic> ParseDiagnostics(
        string output,
        IReadOnlyList<VisualPageSourceMapping> sourceMappings)
    {
        var diagnostics = new List<VisualPageDiagnostic>();
        foreach (var line in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var match = MsBuildDiagnosticRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }
            var sourceLine = int.TryParse(match.Groups["line"].Value, out var value)
                ? value
                : 0;
            var path = match.Groups["path"].Value;
            var mapping = sourceMappings.FirstOrDefault(candidate =>
                sourceLine >= candidate.StartLine
                && sourceLine <= candidate.EndLine
                && path.EndsWith(
                    candidate.RelativePath.Replace('/', Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase));
            diagnostics.Add(new VisualPageDiagnostic
            {
                Code = match.Groups["code"].Value,
                Severity = match.Groups["severity"].Value.Equals(
                    "error",
                    StringComparison.OrdinalIgnoreCase)
                    ? VisualPageDiagnosticSeverity.Error
                    : VisualPageDiagnosticSeverity.Warning,
                Message = match.Groups["message"].Value.Trim(),
                ElementId = mapping?.ElementId,
                RelativePath = path,
                Line = sourceLine == 0 ? null : sourceLine
            });
        }
        return diagnostics;
    }

    [GeneratedRegex("^(?<path>.+?)\\((?<line>\\d+),(?<column>\\d+)\\):\\s*(?<severity>error|warning)\\s+(?<code>[A-Za-z]+\\d+):\\s*(?<message>.*?)(?:\\s+\\[.+\\])?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MsBuildDiagnosticRegex();

    private sealed class SnapshotCounters
    {
        public int Files { get; set; }
        public long Bytes { get; set; }
    }
}

public sealed class VisualPageHostRequest
{
    public int Port { get; init; }
    public string AdapterAddress { get; init; } = "127.0.0.1";
    public bool AllowLan { get; init; }
    public bool TrustCustomCode { get; init; }
}

public sealed class VisualPageHostRuntime
{
    public required int ProcessId { get; init; }
    public required int Port { get; init; }
    public required string Url { get; init; }
    public required string RuntimePath { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public bool IsLanExposed { get; init; }
}

public sealed class VisualPageLocalHostService : IDisposable
{
    private readonly BoundedProcessRunner _runner;
    private readonly HttpClient _healthClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private WindowsProcessLifetimeJob? _job;
    private VisualPageHostRuntime? _runtime;

    public VisualPageLocalHostService(BoundedProcessRunner runner)
    {
        _runner = runner;
    }

    public VisualPageHostRuntime? Runtime => _runtime;

    public async Task<VisualPageHostRuntime> StartAsync(
        VisualPageBuildResult build,
        VisualPageHostRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(request);
        if (build.Outcome != VisualPageBuildOutcome.Succeeded
            || string.IsNullOrWhiteSpace(build.SnapshotPath))
        {
            throw new VisualPageValidationException(
                "A successful latest preview build is required before hosting.");
        }
        if (!request.TrustCustomCode)
        {
            throw new VisualPageValidationException(
                "Compiled preview executes project C# and JavaScript with the current Windows user's permissions. Explicit trust is required.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            StopCore();
            var address = ValidateAddress(request);
            var port = request.Port == 0
                ? ReserveLoopbackPort()
                : request.Port;
            if (port is < 1024 or > 65535)
            {
                throw new VisualPageValidationException(
                    "Choose a host port between 1024 and 65535.");
            }
            if (!IsPortAvailable(address, port))
            {
                throw new VisualPageValidationException(
                    $"Port {port} is already in use on {address}.");
            }

            var dotnet = VisualPagePreviewBuildService.ResolveDotNetExecutable()
                         ?? throw new VisualPageValidationException(
                             ".NET 10 SDK is required for local hosting.");
            var runtimePath = VisualPageProjectService.ResolveProjectPath(
                build.SnapshotPath,
                ".visual-page-runtime");
            if (Directory.Exists(runtimePath))
            {
                var snapshotRoot = Path.GetFullPath(build.SnapshotPath)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!Path.GetFullPath(runtimePath).StartsWith(
                        snapshotRoot,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Runtime output escaped the preview snapshot.");
                }
                Directory.Delete(runtimePath, recursive: true);
            }
            var publish = await _runner.RunAsync(new BoundedProcessRequest
            {
                ExecutablePath = dotnet,
                Arguments =
                [
                    "publish",
                    Path.Combine(build.SnapshotPath, "src", "Host", "Host.csproj"),
                    "--configuration",
                    "Release",
                    "--output",
                    runtimePath,
                    "--nologo"
                ],
                WorkingDirectory = build.SnapshotPath,
                Timeout = TimeSpan.FromMinutes(4),
                MaximumStandardOutputCharacters = 2 * 1024 * 1024,
                MaximumStandardErrorCharacters = 2 * 1024 * 1024
            }, cancellationToken);
            if (publish.ExitCode != 0)
            {
                throw new VisualPageValidationException(
                    "The local host publish failed: " +
                    string.Join(Environment.NewLine,
                        new[] { publish.StandardOutput, publish.StandardError, publish.FailureMessage }
                            .Where(value => !string.IsNullOrWhiteSpace(value))));
            }

            var appDll = Path.Combine(runtimePath, "Host.dll");
            if (!File.Exists(appDll))
            {
                throw new VisualPageValidationException(
                    "The local host output does not contain Host.dll.");
            }
            var url = $"http://{FormatHost(address)}:{port}";
            var startInfo = new ProcessStartInfo
            {
                FileName = dotnet,
                WorkingDirectory = runtimePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add(appDll);
            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add(url);
            var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                process.Dispose();
                throw new VisualPageValidationException(
                    "The local host process did not start.");
            }
            _job ??= WindowsProcessLifetimeJob.Create();
            _job.Assign(process);
            _process = process;
            _runtime = new VisualPageHostRuntime
            {
                ProcessId = process.Id,
                Port = port,
                Url = url + "/",
                RuntimePath = runtimePath,
                StartedAtUtc = DateTimeOffset.UtcNow,
                IsLanExposed = request.AllowLan
            };
            if (!await WaitForHealthAsync(_runtime.Url + "healthz", process, cancellationToken))
            {
                StopCore();
                throw new VisualPageValidationException(
                    "The local host did not pass its loopback health check.");
            }
            return _runtime;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            StopCore();
        }
        finally
        {
            _gate.Release();
        }
    }

    public AtlasMyNetAppDefinition CreateAtlasMyNetDefinition(
        VisualPageProjectSession session,
        VisualPageHostRuntime runtime)
    {
        var dotnet = VisualPagePreviewBuildService.ResolveDotNetExecutable()
                     ?? throw new VisualPageValidationException(
                         ".NET could not be located for the MyNet launch contract.");
        var runtimeUri = new Uri(runtime.Url, UriKind.Absolute);
        var urlTemplate =
            $"{runtimeUri.Scheme}://{FormatHost(runtimeUri.Host)}:{{port}}{runtimeUri.AbsolutePath}";
        return new AtlasMyNetAppDefinition
        {
            Id = $"visual-page.{session.Project.ProjectId}",
            DisplayName = session.Project.Name,
            UrlTemplate = urlTemplate,
            Ports = [runtime.Port],
            Launch = new AtlasMyNetLaunchDefinition
            {
                Executable = dotnet,
                Arguments =
                [
                    Path.Combine(runtime.RuntimePath, "Host.dll"),
                    "--urls",
                    urlTemplate.TrimEnd('/')
                ],
                WorkingDirectory = runtime.RuntimePath
            },
            HealthCheck = new AtlasMyNetHealthCheckDefinition
            {
                Path = "/healthz",
                ExpectedStatusCode = 200,
                TimeoutSeconds = 15
            },
            Stop = new AtlasMyNetStopDefinition
            {
                Mode = AtlasMyNetStopMode.ManagedProcess
            },
            AllowExternalResources = false
        };
    }

    public void RegisterWithAtlasMyNet(
        AtlasMyNetAppStore store,
        AtlasMyNetAppDefinition definition)
    {
        var apps = store.Load().ToList();
        var index = apps.FindIndex(app => string.Equals(
            app.Id,
            definition.Id,
            StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            apps[index] = definition;
        }
        else
        {
            apps.Add(definition);
        }
        store.Save(apps);
    }

    public void Dispose()
    {
        StopCore();
        _healthClient.Dispose();
        _gate.Dispose();
        _job?.Dispose();
        _job = null;
    }

    private async Task<bool> WaitForHealthAsync(
        string healthUrl,
        Process process,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                return false;
            }
            try
            {
                using var response = await _healthClient.GetAsync(
                    healthUrl,
                    cancellationToken);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
            }
            await Task.Delay(250, cancellationToken);
        }
        return false;
    }

    private static string ValidateAddress(VisualPageHostRequest request)
    {
        var address = string.IsNullOrWhiteSpace(request.AdapterAddress)
            ? "127.0.0.1"
            : request.AdapterAddress.Trim();
        if (!IPAddress.TryParse(address, out var parsed))
        {
            throw new VisualPageValidationException(
                "The selected adapter address is invalid.");
        }
        if (!request.AllowLan)
        {
            if (!IPAddress.IsLoopback(parsed))
            {
                throw new VisualPageValidationException(
                    "LAN hosting requires the explicit LAN switch.");
            }
            return parsed.ToString();
        }
        var localAddresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Select(value => value.Address)
            .ToHashSet();
        if (!localAddresses.Contains(parsed) || IPAddress.IsLoopback(parsed))
        {
            throw new VisualPageValidationException(
                "Choose an active non-loopback local adapter for LAN hosting.");
        }
        return parsed.ToString();
    }

    private static int ReserveLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static bool IsPortAvailable(string address, int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Parse(address), port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static string FormatHost(string address) =>
        address.Contains(':', StringComparison.Ordinal)
            ? $"[{address}]"
            : address;

    private void StopCore()
    {
        var process = _process;
        _process = null;
        _runtime = null;
        if (process is null)
        {
            return;
        }
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(3000);
            }
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        process.Dispose();
    }
}
