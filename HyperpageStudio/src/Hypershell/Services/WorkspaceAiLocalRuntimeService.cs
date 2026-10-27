using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Hypershell.Services;

public interface IWorkspaceAiLocalRuntime
{
    bool IsInstalled { get; }

    string AuthorizationToken { get; }

    Task EnsureReadyAsync(
        string modelId,
        bool requireVision,
        CancellationToken cancellationToken = default);
}

public sealed class WorkspaceAiLocalRuntimeService
    : IWorkspaceAiLocalRuntime,
      IDisposable
{
    public const string ExpectedLlamaCppVersion = "b10448";

    private static readonly Uri ModelsEndpoint = new(
        "http://127.0.0.1:11435/v1/models",
        UriKind.Absolute);

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(20);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _healthClient;
    private readonly Queue<string> _recentOutput = new();
    private readonly object _outputGate = new();
    private WindowsProcessLifetimeJob? _processLifetimeJob;
    private Process? _process;
    private string _activeModelId = string.Empty;
    private string _verifiedModelId = string.Empty;
    private bool _activeVisionEnabled;
    private bool _isDisposed;

    public WorkspaceAiLocalRuntimeService()
    {
        AuthorizationToken = Convert.ToHexString(
                RandomNumberGenerator.GetBytes(32))
            .ToLowerInvariant();
        _healthClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            UseCookies = false
        })
        {
            Timeout = TimeSpan.FromSeconds(3)
        };
    }

    public bool IsInstalled => FindExecutable() is not null;

    public string AuthorizationToken { get; }

    public async Task EnsureReadyAsync(
        string modelId,
        bool requireVision,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var model = WorkspaceAiModelCatalog.RequireGenerative(modelId);
        if (requireVision && !model.SupportsVision)
        {
            throw Failure(
                $"{model.DisplayName} cannot inspect computer-control screenshots. Choose a vision-capable local model.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_process is { HasExited: false }
                && string.Equals(
                    _activeModelId,
                    model.Id,
                    StringComparison.Ordinal)
                && string.Equals(
                    _verifiedModelId,
                    model.Id,
                    StringComparison.Ordinal)
                && (!requireVision || _activeVisionEnabled)
                && await IsModelReadyAsync(model.Id, cancellationToken))
            {
                return;
            }

            if (_process is not null)
            {
                StopOwnedProcess();
            }

            if (await IsAnyServerReadyAsync(cancellationToken))
            {
                throw Failure(
                    "Port 11435 is owned by a local model server that this Hypershell window did not start. Close the other Hypershell window or server, then retry; Hypershell will not terminate an unowned process.");
            }

            var executable = FindExecutable()
                             ?? throw Failure(
                                 $"llama.cpp {ExpectedLlamaCppVersion} is not installed. Install package ggml.llamacpp with WinGet first.");
            var verifiedPath =
                await WorkspaceAiModelArtifactService.FindVerifiedPathAsync(
                    model,
                    cancellationToken);
            var visionProjectorPath = requireVision
                ? await WorkspaceAiModelArtifactService
                    .EnsureVerifiedVisionProjectorPathAsync(
                        model,
                        cancellationToken)
                : null;
            Start(
                model,
                executable,
                verifiedPath,
                visionProjectorPath);
            _activeModelId = model.Id;
            _activeVisionEnabled = visionProjectorPath is not null;
            var process = _process
                          ?? throw Failure(
                              "Windows did not retain the managed llama.cpp process.");

            try
            {
                var deadline = DateTimeOffset.UtcNow + StartupTimeout;
                while (DateTimeOffset.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (process.HasExited)
                    {
                        throw Failure(
                            $"llama.cpp stopped while loading {model.DisplayName}.{BuildOutputSuffix()}");
                    }

                    if (await IsModelReadyAsync(model.Id, cancellationToken))
                    {
                        await WorkspaceAiModelArtifactService
                            .RequireVerifiedPathAsync(model, cancellationToken);
                        if (requireVision)
                        {
                            _ = await WorkspaceAiModelArtifactService
                                .FindVerifiedVisionProjectorPathAsync(
                                    model,
                                    cancellationToken)
                                ?? throw Failure(
                                    $"The pinned {model.DisplayName} vision projector disappeared while llama.cpp was loading it.");
                        }

                        _verifiedModelId = model.Id;
                        return;
                    }

                    await Task.Delay(
                        TimeSpan.FromMilliseconds(500),
                        cancellationToken);
                }

                throw Failure(
                    $"llama.cpp did not finish loading {model.DisplayName} within 20 minutes.{BuildOutputSuffix()}");
            }
            catch
            {
                StopOwnedProcess();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        StopOwnedProcess();
        _processLifetimeJob?.Dispose();
        _processLifetimeJob = null;
        _healthClient.Dispose();
        _gate.Dispose();
    }

    private void Start(
        WorkspaceAiModelManifest model,
        string executable,
        string? verifiedModelPath,
        string? visionProjectorPath)
    {
        StopOwnedProcess();
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (verifiedModelPath is null)
        {
            startInfo.ArgumentList.Add("-hf");
            startInfo.ArgumentList.Add(model.HuggingFaceReference);
            startInfo.ArgumentList.Add("--hf-file");
            startInfo.ArgumentList.Add(model.FileName);
        }
        else
        {
            startInfo.ArgumentList.Add("--model");
            startInfo.ArgumentList.Add(verifiedModelPath);
        }
        if (visionProjectorPath is null)
        {
            startInfo.ArgumentList.Add("--no-mmproj");
        }
        else
        {
            startInfo.ArgumentList.Add("--mmproj");
            startInfo.ArgumentList.Add(visionProjectorPath);
        }
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add("11435");
        startInfo.ArgumentList.Add("--alias");
        startInfo.ArgumentList.Add(model.Id);
        startInfo.ArgumentList.Add("--ctx-size");
        startInfo.ArgumentList.Add(model.ContextSize.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--n-gpu-layers");
        startInfo.ArgumentList.Add("all");
        startInfo.ArgumentList.Add("--flash-attn");
        startInfo.ArgumentList.Add("auto");
        startInfo.ArgumentList.Add("--cache-type-k");
        startInfo.ArgumentList.Add("q8_0");
        startInfo.ArgumentList.Add("--cache-type-v");
        startInfo.ArgumentList.Add("q8_0");
        startInfo.ArgumentList.Add("--parallel");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("--no-context-shift");
        startInfo.ArgumentList.Add("--no-webui");
        startInfo.ArgumentList.Add("--api-key");
        startInfo.ArgumentList.Add(AuthorizationToken);
        startInfo.Environment["HF_HOME"] =
            WorkspaceAiModelArtifactService.CacheRoot;

        Directory.CreateDirectory(startInfo.Environment["HF_HOME"]!);
        lock (_outputGate)
        {
            _recentOutput.Clear();
        }

        var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += OnOutputReceived;
        process.ErrorDataReceived += OnOutputReceived;
        if (!process.Start())
        {
            process.Dispose();
            throw Failure("Windows could not start the managed llama.cpp server.");
        }

        try
        {
            _processLifetimeJob ??= WindowsProcessLifetimeJob.Create();
            _processLifetimeJob.Assign(process);
            _process = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception exception) when (exception is
                                         System.ComponentModel.Win32Exception
                                         or InvalidOperationException
                                         or NotSupportedException)
        {
            _process = process;
            StopOwnedProcess();
            throw Failure(
                $"Windows could not attach llama.cpp to Hypershell's crash-safe process boundary. No local model server was left running. {exception.Message}");
        }
    }

    private async Task<bool> IsModelReadyAsync(
        string modelId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _healthClient.GetAsync(
                ModelsEndpoint,
                cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                stream,
                new JsonDocumentOptions { MaxDepth = 16 },
                cancellationToken);
            return document.RootElement.TryGetProperty("data", out var data)
                   && data.ValueKind == JsonValueKind.Array
                   && data.EnumerateArray().Any(item =>
                       item.TryGetProperty("id", out var id)
                       && id.ValueKind == JsonValueKind.String
                       && string.Equals(
                           id.GetString(),
                           modelId,
                           StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is HttpRequestException
                                         or TaskCanceledException
                                         or IOException
                                         or JsonException)
        {
            return false;
        }
    }

    private async Task<bool> IsAnyServerReadyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _healthClient.GetAsync(
                ModelsEndpoint,
                cancellationToken);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                         or TaskCanceledException
                                         or IOException)
        {
            return false;
        }
    }

    private void OnOutputReceived(object sender, DataReceivedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Data))
        {
            return;
        }

        var sanitized = args.Data.Replace(
            AuthorizationToken,
            "[session-token]",
            StringComparison.Ordinal);
        lock (_outputGate)
        {
            _recentOutput.Enqueue(sanitized);
            while (_recentOutput.Count > 12)
            {
                _recentOutput.Dequeue();
            }
        }
    }

    private string BuildOutputSuffix()
    {
        lock (_outputGate)
        {
            if (_recentOutput.Count == 0)
            {
                return string.Empty;
            }

            var detail = string.Join(" | ", _recentOutput)
                .Replace('\r', ' ')
                .Replace('\n', ' ');
            return $" Last runtime messages: {detail[..Math.Min(detail.Length, 1200)]}";
        }
    }

    private void StopOwnedProcess()
    {
        var process = _process;
        _process = null;
        _activeModelId = string.Empty;
        _verifiedModelId = string.Empty;
        _activeVisionEnabled = false;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                         or System.ComponentModel.Win32Exception
                                         or NotSupportedException)
        {
        }
        finally
        {
            process.OutputDataReceived -= OnOutputReceived;
            process.ErrorDataReceived -= OnOutputReceived;
            process.Dispose();
        }
    }

    internal static string? FindExecutable()
    {
        var configured = Environment.GetEnvironmentVariable(
            "HYPERSHELL_LLAMA_SERVER_PATH");
        if (!string.IsNullOrWhiteSpace(configured)
            && Path.IsPathFullyQualified(configured)
            && File.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        var fileName = OperatingSystem.IsWindows()
            ? "llama-server.exe"
            : "llama-server";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH")
                                   ?? string.Empty)
                 .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries
                                             | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (Exception exception) when (exception is ArgumentException
                                             or NotSupportedException
                                             or PathTooLongException)
            {
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var packagesRoot = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WinGet",
            "Packages");
        if (!Directory.Exists(packagesRoot))
        {
            return null;
        }

        try
        {
            return Directory.EnumerateDirectories(
                    packagesRoot,
                    "ggml.llamacpp_*",
                    SearchOption.TopDirectoryOnly)
                .Select(directory => Path.Combine(directory, fileName))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static WorkspaceAiProviderException Failure(string message) =>
        new(message);
}
