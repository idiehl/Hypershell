using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace Hypershell.Services;

/// <summary>Owns private loopback gateways for generated Works without registering or altering the user's live library.</summary>
public sealed class HyperlibWorkPreviewHostService : IDisposable
{
    private sealed record Host(Process Process, Uri Endpoint, string SourceFingerprint);
    private readonly ConcurrentDictionary<string, Host> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _health = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(3) };
    private bool _disposed;

    public static bool IsGeneratedLibrary(string libraryRoot)
    {
        var path = Path.Combine(Path.GetFullPath(libraryRoot), ".studio", "hyperlib-work.json");
        if (!File.Exists(path)) return false;
        RejectLinks(path);
        if (new FileInfo(path).Length > 64 * 1024) return false;
        using var data = JsonDocument.Parse(File.ReadAllText(path));
        return data.RootElement.TryGetProperty("projectKind", out var kind) && kind.GetString() == "Hyperlib"
            && File.Exists(Path.Combine(libraryRoot, "Content", "library.json"));
    }

    public async Task<Uri> EnsureAsync(string libraryRoot, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryRoot));
        RejectLinks(root);
        if (!IsGeneratedLibrary(root)) throw new InvalidOperationException("This folder is not an independent Hyperpage Studio Work.");
        await _gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var sourceFingerprint = await SourceFingerprintAsync(root, ct);
            if (_hosts.TryGetValue(root, out var existing))
            {
                if (!existing.Process.HasExited && existing.SourceFingerprint == sourceFingerprint
                    && await HealthyAsync(existing.Endpoint, root, ct)) return existing.Endpoint;
                _hosts.TryRemove(root, out _);
                Stop(existing.Process);
            }
            var project = Path.Combine(root, "src", "HyperLib.Host", "HyperLib.Host.csproj");
            RejectLinks(project);
            if (!File.Exists(project)) throw new FileNotFoundException("The Work's portable local gateway source is missing. Restore the project SDK folders from your backup.", project);
            int port;
            using (var listener = new TcpListener(System.Net.IPAddress.Loopback, 0)) { listener.Start(); port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; }
            var endpoint = new Uri("http://127.0.0.1:" + port + "/");
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true, RedirectStandardOutput = true
            };
            foreach (var argument in new[] { "run", "--project", project, "--configuration", "Release", "--no-launch-profile", "--", "--root", root, "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture) }) start.ArgumentList.Add(argument);
            start.Environment["DOTNET_NOLOGO"] = "1";
            start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            var diagnostics = new StringBuilder();
            var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            void Capture(object sender, DataReceivedEventArgs args)
            {
                if (args.Data is null) return;
                lock (diagnostics) { diagnostics.AppendLine(args.Data); if (diagnostics.Length > 16000) diagnostics.Remove(0, diagnostics.Length - 16000); }
            }
            process.OutputDataReceived += Capture;
            process.ErrorDataReceived += Capture;
            try
            {
                if (!process.Start()) throw new IOException("The Work's local preview could not start.");
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
                var elapsed = Stopwatch.StartNew();
                while (elapsed.Elapsed < TimeSpan.FromMinutes(3))
                {
                    ct.ThrowIfCancellationRequested();
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (process.HasExited) { lock (diagnostics) throw new InvalidOperationException("The Work's local gateway could not start.\n" + diagnostics); }
                    if (await HealthyAsync(endpoint, root, ct))
                    {
                        _hosts[root] = new(process, endpoint, sourceFingerprint);
                        return endpoint;
                    }
                    await Task.Delay(150, ct);
                }
                lock (diagnostics) throw new TimeoutException("The Work's preview is still building. Check the .NET 10 SDK and package restore.\n" + diagnostics);
            }
            catch { Stop(process); throw; }
        }
        finally { _gate.Release(); }
    }

    private static async Task<string> SourceFingerprintAsync(string root, CancellationToken ct)
    {
        var host = Path.Combine(root, "src", "HyperLib.Host");
        RejectLinks(host);
        var sources = new List<string>
        {
            Path.Combine(host, "Program.cs"), Path.Combine(host, "HyperLib.Host.csproj")
        };
        var pipeline = Path.Combine(host, "StudioPipeline");
        if (Directory.Exists(pipeline))
        {
            RejectLinks(pipeline);
            sources.AddRange(Directory.EnumerateFiles(pipeline, "*.cs", SearchOption.TopDirectoryOnly));
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in sources.Order(StringComparer.OrdinalIgnoreCase))
        {
            RejectLinks(path);
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(host, path)));
            hash.AppendData(await File.ReadAllBytesAsync(path, ct));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private async Task<bool> HealthyAsync(Uri endpoint, string root, CancellationToken ct)
    {
        try
        {
            using var response = await _health.GetAsync(new Uri(endpoint, "api/health"), ct);
            if (!response.IsSuccessStatusCode || !response.Headers.TryGetValues("X-HyperLib-App", out var markers) || !markers.Contains("hyperlib", StringComparer.Ordinal)) return false;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return document.RootElement.TryGetProperty("root", out var actualRoot) && actualRoot.GetString() is { } path
                && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), root, StringComparison.OrdinalIgnoreCase)
                && document.RootElement.TryGetProperty("origin", out var origin) && origin.GetString() == endpoint.GetLeftPart(UriPartial.Authority);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or IOException || error is OperationCanceledException && !ct.IsCancellationRequested) { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var host in _hosts.Values) Stop(host.Process);
        _hosts.Clear();
        _health.Dispose();
        // EnsureAsync may still unwind its gate; leave the semaphore valid for that continuation.
    }
    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        process.Dispose();
    }
    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (File.Exists(current) || Directory.Exists(current)) VisualPageProjectService.RejectReparsePoint(current);
    }
}
