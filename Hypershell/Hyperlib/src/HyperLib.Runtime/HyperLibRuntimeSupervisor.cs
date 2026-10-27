using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HyperLib.Core;

namespace HyperLib.Runtime;

/// <summary>Builds immutable package generations and keeps the last healthy renderer serving while candidates are validated.</summary>
public sealed class HyperLibRuntimeSupervisor : IAsyncDisposable
{
    private sealed class Package(string id, string root)
    {
        public string Id = id, Root = root;
        public SemaphoreSlim Gate = new(1, 1);
        public RuntimeGeneration? Active;
        public TemplateDefinition? Template;
        public string State = "stopped", Error = "", Gateway = "", LastAttempt = "";
        public DateTimeOffset LastLease = DateTimeOffset.UtcNow;
    }
    private readonly ConcurrentDictionary<string, Package> _packages = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Process> _processes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RuntimeGeneration> _generations = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _projectGate = new(1, 1);
    private readonly HttpClient _health = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly WindowsProcessLifetimeJob? _job = OperatingSystem.IsWindows() ? WindowsProcessLifetimeJob.Create() : null;
    private readonly Task _watcher;
    private LibraryState _state = new();
    public string RootPath { get; }
    private readonly string _publicPathBase;
    private readonly string? _gatewayToken;
    private readonly bool _watchSources;
    public event Action<RuntimeGeneration>? GenerationChanged;
    public event Action<RuntimeStatus>? StatusChanged;

    public HyperLibRuntimeSupervisor(string rootPath, string publicPathBase = "", string? gatewayToken = null, bool watchSources = true)
    {
        RootPath = Path.GetFullPath(rootPath);
        if (publicPathBase.Length > 0 && (!publicPathBase.StartsWith('/') || publicPathBase.Contains("..") || publicPathBase.Contains('?') || publicPathBase.Contains('#')))
            throw new ArgumentException("A renderer path base must be an absolute URL path.", nameof(publicPathBase));
        _publicPathBase = publicPathBase.TrimEnd('/');
        _gatewayToken = gatewayToken;
        _watchSources = watchSources;
        _watcher = WatchAsync(_lifetime.Token);
    }

    public async Task EnsureProjectsAsync(LibraryState state, CancellationToken ct = default)
    {
        await _projectGate.WaitAsync(ct);
        try
        {
            // Requests commit in order, but their post-commit continuations may arrive out of order.
            if (state.Revision < _state.Revision) return;
            _state = ContentJson.Clone(state);
            await ProjectScaffolder.EnsureAsync(RootPath, RootPath, "library", "HyperLib", ct);
            RegisterPackage("library", RootPath);
            foreach (var work in _state.Nodes.Where(n => n.Type == "work"))
            {
                var expected = "Works/" + work.Id;
                if (!string.Equals(work.ProjectPath?.Replace('\\', '/'), expected, StringComparison.Ordinal))
                    throw new ContentValidationException($"Work '{work.Title}' has an invalid project path.");
                var project = SafePath(RootPath, expected);
                await ProjectScaffolder.EnsureAsync(RootPath, project, work.Id, work.Title, ct, work.StarterId ?? (work.Id == "animal-atlas" ? "woodland-editorial" : work.Id == "wave-lab" ? "wave-laboratory" : null));
                RegisterPackage(work.Id, project);
            }
        }
        finally { _projectGate.Release(); }
    }

    public TemplateDefinition? GetTemplate(string packageId)
    {
        if (!_packages.TryGetValue(packageId, out var package)) return null;
        if (package.Template is not null) return ContentJson.Clone(package.Template);
        try { return ReadTemplate(package); } catch (Exception ex) when (ex is IOException or JsonException or ContentValidationException) { return null; }
    }

    public RuntimeGeneration? GetRuntime(string packageId) => _packages.TryGetValue(packageId, out var p) ? p.Active : null;
    public RuntimeGeneration? ResolveGeneration(string packageId, string generationId) => _generations.TryGetValue(packageId + ":" + generationId, out var runtime) ? runtime : null;
    public IReadOnlyList<RuntimeStatus> GetStatuses() => _packages.Values.Select(Status).ToList();
    public void Lease(string packageId) { if (_packages.TryGetValue(packageId, out var p)) p.LastLease = DateTimeOffset.UtcNow; }

    /// <summary>Stops a validation-only renderer while retaining its compiled generation for a later reader.</summary>
    public async Task ReleaseRuntimeAsync(string packageId, CancellationToken ct = default)
    {
        if (!_packages.TryGetValue(packageId, out var package)) return;
        await package.Gate.WaitAsync(ct);
        try
        {
            package.LastLease = DateTimeOffset.MinValue;
            if (package.Active is { } active)
            {
                var key = active.PackageId + ":" + active.GenerationId;
                if (_processes.TryRemove(key, out var process)) Stop(process);
                _generations.TryRemove(key, out _);
            }
            package.State = "stopped";
            Notify(package);
        }
        finally { package.Gate.Release(); }
    }

    public Task<RuntimeGeneration> EnsureRuntimeAsync(string packageId, string gatewayBaseUrl, CancellationToken ct = default)
        => EnsureRuntimeCoreAsync(packageId, gatewayBaseUrl, touchLease: true, ct);

    private async Task<RuntimeGeneration> EnsureRuntimeCoreAsync(string packageId, string gatewayBaseUrl, bool touchLease, CancellationToken ct)
    {
        if (!_packages.TryGetValue(packageId, out var package)) throw new ContentValidationException("Unknown renderer package.");
        if (!Uri.TryCreate(gatewayBaseUrl, UriKind.Absolute, out var gateway) || !gateway.IsLoopback || gateway.Scheme != "http")
            throw new ContentValidationException("Renderer gateway must use loopback HTTP.");
        package.Gateway = gatewayBaseUrl.TrimEnd('/');
        if(touchLease) package.LastLease = DateTimeOffset.UtcNow;
        await package.Gate.WaitAsync(ct);
        try
        {
            var fingerprint = Fingerprint(package);
            if (package.Active is { } active && active.GenerationId == fingerprint && IsRunning(active)) return active;
            if (package.LastAttempt != fingerprint || package.Active is null || !IsRunning(package.Active))
                await BuildCandidateAsync(package, fingerprint, ct);
            return package.Active ?? throw new InvalidOperationException(package.Error.Length > 0 ? package.Error : "The renderer is not ready.");
        }
        finally { package.Gate.Release(); }
    }

    private async Task BuildCandidateAsync(Package package, string fingerprint, CancellationToken ct, bool retained = false)
    {
        package.LastAttempt = fingerprint;
        package.State = "building"; package.Error = ""; Notify(package);
        Process? candidate = null;
        var generationKey = package.Id + ":" + fingerprint;
        try
        {
            var generationRoot = SafePath(RootPath, $".hyperlib/runtime/{package.Id}/{fingerprint}");
            var snapshot = Path.Combine(generationRoot, "source");
            var output = Path.Combine(generationRoot, "bin");
            var projectRelative = package.Id == "library" ? "" : Path.GetRelativePath(RootPath, package.Root);
            var snapshotProject = Path.Combine(snapshot, projectRelative);
            var template = retained ? ReadTemplateFile(Path.Combine(snapshotProject, package.Id == "library" ? "Presentation/authoring.template.json" : "authoring.template.json")) : ReadTemplate(package);
            ValidateExistingContent(package, template);
            if (!File.Exists(Path.Combine(output, "release.ready.json")))
            {
                if(retained) throw new InvalidOperationException("The retained renderer release is incomplete.");
                foreach (var file in SourceFiles(package))
                {
                    ct.ThrowIfCancellationRequested();
                    var destination = SafePath(snapshot, Path.GetRelativePath(RootPath, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination, overwrite: true);
                }
                if (Fingerprint(package) != fingerprint) { package.State = package.Active is null ? "pending" : "active"; return; }
                var result = await PublishAsync(Path.Combine(snapshotProject, "src/Host/Host.csproj"), output, snapshot, ct);
                if (result.ExitCode != 0) throw new InvalidOperationException("Template build failed. " + result.Output);
                await File.WriteAllTextAsync(Path.Combine(output,"release.ready.json"), JsonSerializer.Serialize(new { fingerprint }, ContentJson.Options), ct);
            }
            var prefix = $"{_publicPathBase}/_runtime/{package.Id}/{fingerprint}";
            var port = ReservePort();
            var upstream = $"http://127.0.0.1:{port}";
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = output, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(output, "Host.dll")); start.ArgumentList.Add("--urls"); start.ArgumentList.Add(upstream);
            start.Environment["HYPERLIB_PACKAGE"] = package.Id;
            start.Environment["HYPERLIB_GENERATION"] = fingerprint;
            start.Environment["HYPERLIB_GATEWAY"] = package.Gateway;
            if (_gatewayToken is not null) start.Environment["HYPERLIB_GATEWAY_TOKEN"] = _gatewayToken;
            else start.Environment.Remove("HYPERLIB_GATEWAY_TOKEN");
            start.Environment["HYPERLIB_PATH_BASE"] = prefix;
            start.Environment["HYPERLIB_TEMPLATE"] = Path.Combine(snapshotProject, package.Id == "library" ? "Presentation/authoring.template.json" : "authoring.template.json");
            foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("ATLAS_ARCHIVES_", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.Environment.Remove(key);
            candidate = Process.Start(start) ?? throw new InvalidOperationException("Renderer process could not start.");
            _job?.Assign(candidate);
            var logs = new StringBuilder();
            candidate.OutputDataReceived += (_, e) => { if (e.Data is not null) lock(logs) { if(logs.Length < 16000) logs.AppendLine(e.Data); } };
            candidate.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock(logs) { if(logs.Length < 16000) logs.AppendLine(e.Data); } };
            candidate.BeginOutputReadLine(); candidate.BeginErrorReadLine();
            var healthy = false;
            for (var i = 0; i < 60; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (candidate.HasExited) throw new InvalidOperationException("Renderer exited during startup. " + logs);
                try { using var response = await _health.GetAsync(upstream + prefix + "/healthz", ct); if (response.IsSuccessStatusCode) { healthy = true; break; } }
                catch (HttpRequestException) { }
                await Task.Delay(250, ct);
            }
            if (!healthy) throw new InvalidOperationException("Renderer did not become healthy.");
            // Exercise actual server-side rendering, including user C# and the current content contract.
            var sample = _state.Pages.Values.FirstOrDefault(p => p.WorkId == package.Id);
            var smokePath = sample is null ? "/browse/" + Uri.EscapeDataString(package.Id) : "/page/" + Uri.EscapeDataString(sample.Id);
            using (var smoke = await _health.GetAsync(upstream + prefix + smokePath, ct))
            {
                smoke.EnsureSuccessStatusCode();
                var body = await smoke.Content.ReadAsStringAsync(ct);
                if (body.Contains("This component could not render:", StringComparison.Ordinal) || body.Contains("class=\"hl-error\"", StringComparison.Ordinal))
                    throw new InvalidOperationException("Renderer started but failed the content rendering check.");
            }
            if (!retained && Fingerprint(package) != fingerprint) { package.State = package.Active is null ? "pending" : "active"; return; }
            ValidateExistingContent(package, template);
            var runtime = new RuntimeGeneration(package.Id, fingerprint, prefix + "/", upstream, output, candidate.Id, DateTimeOffset.UtcNow);
            var previous = package.Active;
            _processes[generationKey] = candidate; _generations[generationKey] = runtime; candidate = null;
            package.Active = runtime; package.Template = template; package.State = "active"; package.Error = "";
            Directory.CreateDirectory(Path.Combine(RootPath, ".hyperlib/runtime", package.Id));
            var activeFile = Path.Combine(RootPath, ".hyperlib/runtime", package.Id, "active.json");
            await File.WriteAllTextAsync(activeFile + ".tmp", JsonSerializer.Serialize(runtime, ContentJson.Options), ct);
            File.Move(activeFile + ".tmp", activeFile, overwrite: true);
            Notify(package); GenerationChanged?.Invoke(runtime);
            if (previous is not null && previous.GenerationId != runtime.GenerationId) _ = RetireAsync(previous, _lifetime.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancellation is not a failed source revision. A later preview must retry this exact saved source.
            package.LastAttempt = "";
            package.State = package.Active is null ? "stopped" : "active";
            Notify(package);
            throw;
        }
        catch (Exception ex)
        {
            var error = ex.Message.Length > 16000 ? ex.Message[..16000] : ex.Message;
            if(!retained && package.Active is null && SavedGeneration(package) is { } saved && saved != fingerprint)
                await BuildCandidateAsync(package, saved, ct, retained: true);
            package.LastAttempt = fingerprint;
            package.Error = error; package.State = package.Active is null ? "failed" : "active-with-errors"; Notify(package);
        }
        finally { if (candidate is not null) Stop(candidate); }
    }

    private async Task WatchAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(ct))
                foreach (var package in _packages.Values.Where(p => p.Gateway.Length > 0))
                {
                    if (DateTimeOffset.UtcNow - package.LastLease > TimeSpan.FromMinutes(5))
                    {
                        if(package.Active is { } idle && _processes.TryRemove(idle.PackageId + ":" + idle.GenerationId, out var process)) { Stop(process); package.State = "stopped"; Notify(package); }
                        continue;
                    }
                    if (_watchSources)
                        try { await EnsureRuntimeCoreAsync(package.Id, package.Gateway, touchLease: false, ct); } catch (Exception ex) when (ex is not OperationCanceledException) { package.Error = ex.Message; Notify(package); }
                }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private TemplateDefinition ReadTemplate(Package package)
    {
        var path = SafePath(package.Root, package.Id == "library" ? "Presentation/authoring.template.json" : "authoring.template.json");
        return ReadTemplateFile(path);
    }
    private void ValidateExistingContent(Package package, TemplateDefinition template)
    {
        var appearances = package.Id == "library"
            ? _state.Nodes.Where(n=>n.Type is "collection" or "series").Select(n=>n.Appearance).Prepend(_state.Appearance)
            : _state.Nodes.Where(n=>n.WorkId == package.Id || n.Id == package.Id).Select(n=>n.Appearance);
        TemplateValidator.EnsureCompatible(template, _state.Pages.Values.Where(p=>p.WorkId==package.Id), appearances);
    }
    private static TemplateDefinition ReadTemplateFile(string path)
    {
        var template = JsonSerializer.Deserialize<TemplateDefinition>(File.ReadAllText(path), ContentJson.Options) ?? throw new ContentValidationException("Template is empty.");
        TemplateValidator.ValidateDefinition(template);
        template.Appearance = StandardAppearance.WithDefaults(template.Appearance).ToList();
        return template;
    }

    private void RegisterPackage(string id, string root)
    {
        if(_packages.ContainsKey(id)) return;
        var package = new Package(id, root);
        if(SavedGeneration(package) is { } saved)
        {
            try
            {
                var relative = id == "library" ? "Presentation/authoring.template.json" : $"Works/{id}/authoring.template.json";
                package.Template = ReadTemplateFile(SafePath(RootPath,$".hyperlib/runtime/{id}/{saved}/source/{relative}"));
            }
            catch(Exception ex) when(ex is IOException or JsonException or ContentValidationException) { package.Error=ex.Message; }
        }
        _packages.TryAdd(id,package);
    }
    private string? SavedGeneration(Package package)
    {
        try
        {
            var path=SafePath(RootPath,$".hyperlib/runtime/{package.Id}/active.json");
            if(!File.Exists(path))return null;
            var saved=JsonSerializer.Deserialize<RuntimeGeneration>(File.ReadAllText(path),ContentJson.Options);
            return saved?.PackageId == package.Id && saved.GenerationId.Length == 24 && saved.GenerationId.All(Uri.IsHexDigit) ? saved.GenerationId : null;
        }
        catch(Exception ex) when(ex is IOException or JsonException or ContentValidationException) { return null; }
    }

    private IEnumerable<string> SourceFiles(Package package)
    {
        var roots = new[] { Path.Combine(RootPath, "src/HyperLib.Runtime"), Path.Combine(RootPath, "src/HyperLib.Core"), Path.Combine(package.Root, "src/Host"), Path.Combine(package.Root, "src/Pages"), Path.Combine(package.Root, "Shared"), Path.Combine(package.Root, "Pages") };
        var files = roots.Where(Directory.Exists).SelectMany(r => EnumerateSources(r)).ToList();
        var templates = Path.Combine(package.Root, "Templates");
        if (Directory.Exists(templates)) files.AddRange(EnumerateSources(templates, excludeLibraryCatalog: package.Id == "library"));
        files.Add(SafePath(package.Root, package.Id == "library" ? "Presentation/authoring.template.json" : "authoring.template.json"));
        var manifest = Path.Combine(package.Root, "visual-page.project.json"); if (File.Exists(manifest)) files.Add(manifest);
        return files.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
    }
    private IEnumerable<string> EnumerateSources(string directory, bool excludeLibraryCatalog = false)
    {
        RejectLink(directory);
        foreach(var file in Directory.EnumerateFiles(directory)) { RejectLink(file); yield return file; }
        foreach(var child in Directory.EnumerateDirectories(directory))
        {
            if (Path.GetFileName(child) is "bin" or "obj" or ".git" or "node_modules") continue;
            // Only the library's top-level Templates folder doubles as the reusable catalog.
            // Work-owned Templates and ordinary library presentation fragments remain source.
            if (excludeLibraryCatalog)
            {
                RejectLink(child);
                if (Path.GetFileName(child).StartsWith(".pending-", StringComparison.OrdinalIgnoreCase)
                    || File.Exists(Path.Combine(child, "template.json"))) continue;
            }
            foreach(var file in EnumerateSources(child)) yield return file;
        }
    }
    private string Fingerprint(Package package)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long bytes = 0; var count = 0;
        foreach(var file in SourceFiles(package))
        {
            var info = new FileInfo(file); bytes += info.Length;
            if (++count > 10000 || bytes > 128L * 1024 * 1024) throw new ContentValidationException("Renderer sources exceed the bounded build size.");
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(RootPath, file).Replace('\\','/')));
            hash.AppendData(File.ReadAllBytes(file));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset())[..24];
    }
    private static async Task<(int ExitCode, string Output)> PublishAsync(string project, string output, string cwd, CancellationToken ct)
    {
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach(var arg in new[]{"publish", project, "-c", "Release", "-o", output, "--nologo", "-v", "minimal"}) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("The .NET 10 SDK could not be started.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(4));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token); var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); } catch { if(!process.HasExited) process.Kill(entireProcessTree:true); throw; }
        var text = await stdout + "\n" + await stderr; return (process.ExitCode, text.Length > 16000 ? text[^16000..] : text);
    }
    private async Task RetireAsync(RuntimeGeneration generation, CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch(OperationCanceledException) { }
        var key = generation.PackageId + ":" + generation.GenerationId;
        if (_processes.TryRemove(key, out var process)) Stop(process);
        _generations.TryRemove(key, out _);
    }
    private bool IsRunning(RuntimeGeneration generation) => _processes.TryGetValue(generation.PackageId + ":" + generation.GenerationId, out var process) && !process.HasExited;
    private static int ReservePort() { var listener = new TcpListener(IPAddress.Loopback,0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    private static void Stop(Process process) { try { if(!process.HasExited) process.Kill(entireProcessTree:true); } catch(InvalidOperationException) { } finally { process.Dispose(); } }
    private static RuntimeStatus Status(Package p) => new(p.Id, p.State, string.IsNullOrEmpty(p.Error) ? null : p.Error, p.Active);
    private void Notify(Package package) => StatusChanged?.Invoke(Status(package));
    private static void RejectLink(string path) { if(File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new ContentValidationException("Renderer source links are not supported."); }
    private static string SafePath(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new ContentValidationException("Paths must be relative.");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if(!full.StartsWith(fullRoot,StringComparison.OrdinalIgnoreCase)) throw new ContentValidationException("Path escaped the library.");
        var current = fullRoot.TrimEnd(Path.DirectorySeparatorChar); if(Directory.Exists(current)) RejectLink(current);
        foreach(var part in Path.GetRelativePath(fullRoot,full).Split(Path.DirectorySeparatorChar)) { current=Path.Combine(current,part); if(File.Exists(current)||Directory.Exists(current)) RejectLink(current); }
        return full;
    }
    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel(); await _watcher;
        foreach(var process in _processes.Values) Stop(process);
        _processes.Clear(); _job?.Dispose(); _health.Dispose(); _lifetime.Dispose();
    }
}
