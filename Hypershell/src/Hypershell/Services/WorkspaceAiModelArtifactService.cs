using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace Hypershell.Services;

internal static class WorkspaceAiModelArtifactService
{
    private static readonly ConcurrentDictionary<
        string,
        (long Length, long LastWriteTicks)> VerifiedArtifacts =
            new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim VisionProjectorDownloadGate =
        new(1, 1);
    private static readonly HttpClient ArtifactClient = new(
        new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            UseCookies = false
        })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    public static string CacheRoot => Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "Hypershell",
        "ai",
        "huggingface");

    public static Task<string?> FindVerifiedPathAsync(
        WorkspaceAiModelManifest model,
        CancellationToken cancellationToken = default) =>
        FindVerifiedArtifactPathAsync(
            model,
            new WorkspaceAiModelArtifactManifest(
                model.FileName,
                model.FileSizeBytes,
                model.Sha256),
            model.DisplayName,
            cancellationToken);

    public static async Task<string?> FindVerifiedVisionProjectorPathAsync(
        WorkspaceAiModelManifest model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.VisionProjector is null)
        {
            return null;
        }

        return await FindVerifiedArtifactPathAsync(
            model,
            model.VisionProjector,
            $"{model.DisplayName} vision projector",
            cancellationToken);
    }

    public static async Task<string> EnsureVerifiedVisionProjectorPathAsync(
        WorkspaceAiModelManifest model,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        var artifact = model.VisionProjector
                       ?? throw Failure(
                           $"{model.DisplayName} does not include a pinned vision projector.");
        var existing = await FindVerifiedArtifactPathAsync(
            model,
            artifact,
            $"{model.DisplayName} vision projector",
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        await VisionProjectorDownloadGate.WaitAsync(cancellationToken);
        try
        {
            existing = await FindVerifiedArtifactPathAsync(
                model,
                artifact,
                $"{model.DisplayName} vision projector",
                cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            await DownloadPinnedArtifactAsync(
                model,
                artifact,
                cancellationToken);
            return await FindVerifiedArtifactPathAsync(
                       model,
                       artifact,
                       $"{model.DisplayName} vision projector",
                       cancellationToken)
                   ?? throw Failure(
                       $"The pinned {model.DisplayName} vision projector was not found after download.");
        }
        finally
        {
            VisionProjectorDownloadGate.Release();
        }
    }

    private static async Task<string?> FindVerifiedArtifactPathAsync(
        WorkspaceAiModelManifest model,
        WorkspaceAiModelArtifactManifest artifact,
        string displayName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(artifact);
        // Hugging Face cache repository names use a double separator only at
        // the namespace boundary. Build that spelling explicitly instead of
        // allowing model-name hyphens to be rewritten.
        var path = GetArtifactPath(model, artifact.FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        var info = new FileInfo(path);
        if (!string.IsNullOrWhiteSpace(info.LinkTarget))
        {
            var resolved = File.ResolveLinkTarget(path, returnFinalTarget: true)
                           ?? throw Failure(
                               $"The cached {displayName} artifact link could not be resolved.");
            var cacheRoot = Path.GetFullPath(CacheRoot)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var resolvedPath = Path.GetFullPath(resolved.FullName);
            if (!resolvedPath.StartsWith(
                    cacheRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw Failure(
                    $"The cached {displayName} artifact link escaped Hypershell's model cache.");
            }

            path = resolvedPath;
            info = new FileInfo(path);
        }

        if (info.Length != artifact.FileSizeBytes)
        {
            throw Failure(
                $"The cached {displayName} artifact has an unexpected byte size. Hypershell refused to load it.");
        }

        var signature = (info.Length, info.LastWriteTimeUtc.Ticks);
        if (VerifiedArtifacts.TryGetValue(path, out var verified)
            && verified == signature)
        {
            return path;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken))
            .ToLowerInvariant();
        if (!string.Equals(hash, artifact.Sha256, StringComparison.Ordinal))
        {
            throw Failure(
                $"The cached {displayName} artifact failed SHA-256 validation. Hypershell refused to load it.");
        }

        VerifiedArtifacts[path] = signature;
        return path;
    }

    public static async Task<string> RequireVerifiedPathAsync(
        WorkspaceAiModelManifest model,
        CancellationToken cancellationToken = default) =>
        await FindVerifiedPathAsync(model, cancellationToken)
        ?? throw Failure(
            $"The pinned {model.DisplayName} artifact was not found after download. Expected revision {model.Revision} and file {model.FileName}.");

    private static string GetArtifactPath(
        WorkspaceAiModelManifest model,
        string fileName) =>
        Path.Combine(
            CacheRoot,
            "hub",
            $"models--{model.HuggingFaceRepository.Replace("/", "--", StringComparison.Ordinal)}",
            "snapshots",
            model.Revision,
            fileName);

    private static async Task DownloadPinnedArtifactAsync(
        WorkspaceAiModelManifest model,
        WorkspaceAiModelArtifactManifest artifact,
        CancellationToken cancellationToken)
    {
        var destination = GetArtifactPath(model, artifact.FileName);
        var directory = Path.GetDirectoryName(destination)
                        ?? throw Failure(
                            "The local AI artifact cache path is invalid.");
        Directory.CreateDirectory(directory);
        var partial = destination
                      + $".{Guid.NewGuid():N}.partial";
        try
        {
            var uri = new Uri(
                $"https://huggingface.co/{model.HuggingFaceRepository}/resolve/{model.Revision}/{Uri.EscapeDataString(artifact.FileName)}?download=true",
                UriKind.Absolute);
            using var response = await ArtifactClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw Failure(
                    $"The pinned {model.DisplayName} vision projector download returned HTTP {(int)response.StatusCode}.");
            }

            if (response.Content.Headers.ContentLength is long contentLength
                && contentLength != artifact.FileSizeBytes)
            {
                throw Failure(
                    $"The pinned {model.DisplayName} vision projector download reported an unexpected byte size.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            await using var destinationStream = new FileStream(
                partial,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024];
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(
                    buffer.AsMemory(),
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > artifact.FileSizeBytes)
                {
                    throw Failure(
                        $"The pinned {model.DisplayName} vision projector exceeded its manifest size.");
                }

                hash.AppendData(buffer, 0, read);
                await destinationStream.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken);
            }

            Array.Clear(buffer);
            await destinationStream.FlushAsync(cancellationToken);
            if (total != artifact.FileSizeBytes
                || !string.Equals(
                    Convert.ToHexString(hash.GetHashAndReset())
                        .ToLowerInvariant(),
                    artifact.Sha256,
                    StringComparison.Ordinal))
            {
                throw Failure(
                    $"The pinned {model.DisplayName} vision projector failed manifest validation.");
            }

            // Windows will not atomically rename an open file. Dispose the
            // verified writer before publishing the artifact into the pinned
            // snapshot path; the await-using cleanup remains idempotent.
            await destinationStream.DisposeAsync();

            try
            {
                File.Move(partial, destination, overwrite: false);
            }
            catch (IOException) when (File.Exists(destination))
            {
                File.Delete(partial);
            }
        }
        catch (WorkspaceAiProviderException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                         or IOException
                                         or UnauthorizedAccessException)
        {
            throw Failure(
                $"The pinned {model.DisplayName} vision projector could not be downloaded safely.",
                exception);
        }
        finally
        {
            try
            {
                if (File.Exists(partial))
                {
                    File.Delete(partial);
                }
            }
            catch (Exception exception) when (exception is IOException
                                             or UnauthorizedAccessException)
            {
            }
        }
    }

    private static WorkspaceAiProviderException Failure(
        string message,
        Exception? innerException = null) =>
        new(message, innerException);
}
