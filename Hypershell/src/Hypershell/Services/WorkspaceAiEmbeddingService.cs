using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Hypershell.Services;

public interface IWorkspaceAiEmbeddingService
{
    bool IsInstalled { get; }

    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}

public sealed class WorkspaceAiEmbeddingService
    : IWorkspaceAiEmbeddingService,
      IDisposable
{
    public const int EmbeddingDimension = 1024;
    public const int MaximumBatchSize = 64;
    public const int MaximumTextCharacters = 4096;
    public const int MaximumBatchCharacters = 128 * 1024;
    public const int MaximumResponseBytes = 16 * 1024 * 1024;

    private static readonly Uri ModelsEndpoint = new(
        "http://127.0.0.1:11436/v1/models",
        UriKind.Absolute);
    private static readonly Uri EmbeddingsEndpoint = new(
        "http://127.0.0.1:11436/v1/embeddings",
        UriKind.Absolute);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(20);

    private readonly SemaphoreSlim _startupGate = new(1, 1);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly HttpClient _httpClient;
    private readonly Queue<string> _recentOutput = new();
    private readonly object _outputGate = new();
    private readonly string _authorizationToken;
    private Process? _process;
    private bool _artifactVerified;
    private bool _isDisposed;

    public WorkspaceAiEmbeddingService()
    {
        _authorizationToken = Convert.ToHexString(
                RandomNumberGenerator.GetBytes(32))
            .ToLowerInvariant();
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            UseCookies = false
        })
        {
            Timeout = TimeSpan.FromMinutes(3)
        };
    }

    public bool IsInstalled =>
        WorkspaceAiLocalRuntimeService.FindExecutable() is not null;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count is < 1 or > MaximumBatchSize
            || texts.Any(text => string.IsNullOrWhiteSpace(text)
                                 || text.Length > MaximumTextCharacters
                                 || text.IndexOf('\0') >= 0)
            || texts.Sum(text => text.Length) > MaximumBatchCharacters)
        {
            throw Failure(
                $"Embedding input must contain 1-{MaximumBatchSize} bounded, non-empty texts.");
        }

        await EnsureReadyAsync(cancellationToken);
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                model = WorkspaceAiModelCatalog.LibrarianModelId,
                input = texts,
                encoding_format = "float"
            });
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                EmbeddingsEndpoint)
            {
                Content = new ByteArrayContent(payload)
            };
            request.Content.Headers.ContentType =
                new MediaTypeHeaderValue("application/json");
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                _authorizationToken);
            request.Headers.UserAgent.ParseAdd("Hypershell/0.1");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw Failure(
                    $"The local Librarian returned HTTP {(int)response.StatusCode}.");
            }

            await response.Content.LoadIntoBufferAsync(
                MaximumResponseBytes,
                cancellationToken);
            await using var stream = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                stream,
                new JsonDocumentOptions { MaxDepth = 8 },
                cancellationToken);
            return ParseEmbeddings(document.RootElement, texts.Count);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WorkspaceAiProviderException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                         or IOException
                                         or JsonException
                                         or TaskCanceledException)
        {
            throw Failure(
                "The local Librarian embedding request could not be completed.",
                exception);
        }
        finally
        {
            _requestGate.Release();
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
        _httpClient.Dispose();
        _startupGate.Dispose();
        _requestGate.Dispose();
    }

    private async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        await _startupGate.WaitAsync(cancellationToken);
        try
        {
            if (_process is { HasExited: false }
                && _artifactVerified
                && await IsReadyAsync(cancellationToken))
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
                    "Port 11436 is already occupied by another local model server. Stop it before using the Hyperlib Librarian.");
            }

            var model = WorkspaceAiModelCatalog.RequireEmbedding(
                WorkspaceAiModelCatalog.LibrarianModelId);
            var executable = WorkspaceAiLocalRuntimeService.FindExecutable()
                             ?? throw Failure(
                                 $"llama.cpp {WorkspaceAiLocalRuntimeService.ExpectedLlamaCppVersion} is not installed.");
            var verifiedPath =
                await WorkspaceAiModelArtifactService.FindVerifiedPathAsync(
                    model,
                    cancellationToken);
            Start(model, executable, verifiedPath);
            var process = _process
                          ?? throw Failure(
                              "Windows did not retain the managed Librarian process.");
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

                    if (await IsReadyAsync(cancellationToken))
                    {
                        await WorkspaceAiModelArtifactService
                            .RequireVerifiedPathAsync(model, cancellationToken);
                        _artifactVerified = true;
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
            _startupGate.Release();
        }
    }

    private void Start(
        WorkspaceAiModelManifest model,
        string executable,
        string? verifiedModelPath)
    {
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

        AddArgument(startInfo, "--no-mmproj");
        AddArgument(startInfo, "--embedding");
        AddArgument(startInfo, "--pooling", "last");
        AddArgument(startInfo, "--host", "127.0.0.1");
        AddArgument(startInfo, "--port", "11436");
        AddArgument(startInfo, "--alias", model.Id);
        AddArgument(startInfo, "--ctx-size", model.ContextSize.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(startInfo, "--n-gpu-layers", "0");
        AddArgument(startInfo, "--ubatch-size", "8192");
        AddArgument(startInfo, "--parallel", "1");
        AddArgument(startInfo, "--no-webui");
        AddArgument(startInfo, "--api-key", _authorizationToken);
        startInfo.Environment["HF_HOME"] =
            WorkspaceAiModelArtifactService.CacheRoot;
        Directory.CreateDirectory(WorkspaceAiModelArtifactService.CacheRoot);

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
            throw Failure(
                "Windows could not start the managed Librarian server.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;
    }

    private async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
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
                new JsonDocumentOptions { MaxDepth = 8 },
                cancellationToken);
            return document.RootElement.TryGetProperty("data", out var data)
                   && data.ValueKind == JsonValueKind.Array
                   && data.EnumerateArray().Any(item =>
                       item.TryGetProperty("id", out var id)
                       && id.ValueKind == JsonValueKind.String
                       && string.Equals(
                           id.GetString(),
                           WorkspaceAiModelCatalog.LibrarianModelId,
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
            using var response = await _httpClient.GetAsync(
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

    private static IReadOnlyList<float[]> ParseEmbeddings(
        JsonElement root,
        int expectedCount)
    {
        if (!root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array
            || data.GetArrayLength() != expectedCount)
        {
            throw Failure(
                "The local Librarian returned an invalid embedding count.");
        }

        var result = new float[expectedCount][];
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("index", out var indexElement)
                || !indexElement.TryGetInt32(out var index)
                || index < 0
                || index >= expectedCount
                || result[index] is not null
                || !item.TryGetProperty("embedding", out var embedding)
                || embedding.ValueKind != JsonValueKind.Array
                || embedding.GetArrayLength() != EmbeddingDimension)
            {
                throw Failure(
                    "The local Librarian returned a malformed embedding item.");
            }

            var vector = new float[EmbeddingDimension];
            var squaredLength = 0d;
            var position = 0;
            foreach (var value in embedding.EnumerateArray())
            {
                if (!value.TryGetSingle(out var component)
                    || !float.IsFinite(component))
                {
                    throw Failure(
                        "The local Librarian returned a non-finite embedding value.");
                }

                vector[position++] = component;
                squaredLength += component * component;
            }

            if (squaredLength <= double.Epsilon)
            {
                throw Failure(
                    "The local Librarian returned an empty embedding vector.");
            }

            var scale = (float)(1d / Math.Sqrt(squaredLength));
            for (var vectorIndex = 0;
                 vectorIndex < vector.Length;
                 vectorIndex++)
            {
                vector[vectorIndex] *= scale;
            }

            result[index] = vector;
        }

        if (result.Any(vector => vector is null))
        {
            throw Failure(
                "The local Librarian omitted an embedding item.");
        }

        return result!;
    }

    private void OnOutputReceived(object sender, DataReceivedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Data))
        {
            return;
        }

        var sanitized = args.Data.Replace(
            _authorizationToken,
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
        _artifactVerified = false;
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

    private static void AddArgument(
        ProcessStartInfo startInfo,
        string name,
        string? value = null)
    {
        startInfo.ArgumentList.Add(name);
        if (value is not null)
        {
            startInfo.ArgumentList.Add(value);
        }
    }

    private static WorkspaceAiProviderException Failure(
        string message,
        Exception? innerException = null) =>
        new(message, innerException);
}
