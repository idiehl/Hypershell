using Hypershell.Models;
using System.IO;
using System.IO.Enumeration;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

public enum WorkspaceAiOperationStatus
{
    Succeeded,
    Denied,
    ConfirmationRequired,
    Failed
}

public sealed record WorkspaceAiOperationResult<T>
{
    public required WorkspaceAiOperationStatus Status { get; init; }
    public required string Message { get; init; }
    public T? Value { get; init; }
    public bool Succeeded => Status == WorkspaceAiOperationStatus.Succeeded;
}

public sealed record WorkspaceAiDirectoryEntry(
    string Name,
    string RelativePath,
    bool IsDirectory,
    long Size,
    DateTimeOffset LastWriteTimeUtc);

public sealed record WorkspaceAiDirectoryListing(
    string RelativePath,
    IReadOnlyList<WorkspaceAiDirectoryEntry> Entries,
    bool IsTruncated);

public sealed record WorkspaceAiTextSearchMatch(
    string Path,
    string DisplayPath,
    int Line,
    int Column,
    string Preview);

public sealed record WorkspaceAiTextSearchResult(
    string Root,
    string Query,
    string FilePattern,
    IReadOnlyList<WorkspaceAiTextSearchMatch> Matches,
    int FilesScanned,
    int FilesSkipped,
    long BytesScanned,
    bool IsTruncated);

public sealed record WorkspaceAiTextFile(
    string RelativePath,
    string Content,
    string LanguageId,
    DocumentEncodingKind EncodingKind,
    DocumentLineEndingKind LineEndingKind,
    string Sha256,
    long ByteLength);

public sealed record WorkspaceAiWriteReceipt(
    string RelativePath,
    string Sha256,
    long ByteLength,
    bool WasCreated);

public sealed record WorkspaceAiTextTargetState(
    string Target,
    bool Exists,
    string CurrentContent,
    string? CurrentSha256,
    long ByteLength);

public sealed record WorkspaceAiDeleteReceipt(
    string Path,
    string Sha256,
    long ByteLength);

public sealed record WorkspaceAiNetworkTextSnapshot(
    string Url,
    string ContentType,
    string Content,
    int StatusCode);

public sealed record WorkspaceAiTerminalDispatchReceipt(
    bool Executed,
    string CommandSha256);

public sealed record WorkspaceAiTerminalLineSnapshot(
    string Kind,
    string Text);

public sealed record WorkspaceAiTerminalStateSnapshot(
    string Title,
    string ShellKind,
    string WorkingDirectory,
    string Prompt,
    bool IsRunning,
    string DraftCommand,
    IReadOnlyList<WorkspaceAiTerminalLineSnapshot> Transcript,
    bool IsTranscriptTruncated);

public sealed record WorkspaceAiOpenDocumentSnapshot(
    string SessionId,
    string Title,
    string RelativePath,
    string Content,
    string LanguageId,
    string ProviderId,
    string ProviderSummary,
    IReadOnlyList<DocumentProviderDiagnostic> Diagnostics,
    string Sha256,
    bool HasUnsavedChanges);

public sealed record WorkspaceAiOpenDocumentMutationReceipt(
    string SessionId,
    string RelativePath,
    string Sha256,
    int CharacterLength,
    string ProviderMessage);

public sealed class WorkspaceAiOperationService
{
    public const long MaximumTextBytes = 4L * 1024L * 1024L;
    public const int MaximumDirectoryEntries = 512;
    public const int MaximumSearchMatches = 120;
    public const int MaximumSearchFiles = 2500;
    public const int MaximumSearchEntries = 20_000;
    public const int MaximumSearchDirectoryEntries = 4096;
    public const long MaximumSearchFileBytes = 1024L * 1024L;
    public const long MaximumSearchTotalBytes = 32L * 1024L * 1024L;
    public const int MaximumCommandCharacters = 32 * 1024;
    public const int MaximumTerminalTranscriptLines = 400;
    public const int MaximumTerminalTranscriptCharacters = 128 * 1024;
    public const int MaximumNetworkTextBytes = 512 * 1024;

    private static readonly HttpClient DefaultNetworkClient =
        CreateNetworkClient();

    private static readonly HashSet<string> SearchExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".hg", ".svn", ".vs", ".idea", ".hypershell",
            "node_modules", "bin", "obj", "dist", "coverage", ".next",
            ".nuxt", "target", "vendor", "packages"
        };

    private static readonly HashSet<string> SearchExcludedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".7z", ".a", ".avi", ".bin", ".bmp", ".class", ".db",
            ".dll", ".dylib", ".exe", ".gif", ".gz", ".ico", ".jar",
            ".jpeg", ".jpg", ".lockb", ".mov", ".mp3", ".mp4", ".o",
            ".obj", ".pdf", ".png", ".pdb", ".so", ".sqlite", ".tar",
            ".tiff", ".ttf", ".wav", ".webp", ".woff", ".woff2", ".zip"
        };

    private readonly WorkspaceAiPermissionBroker _broker;
    private readonly WorkspaceAiAuditService _audit;
    private readonly TextDocumentService _documentService;
    private readonly WorkspaceFileSystemProviderRegistry? _workspaceProviders;
    private readonly TimeProvider _timeProvider;
    private readonly HttpClient _networkClient;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly AsyncLocal<string?> _auditConversationId = new();

    public WorkspaceAiOperationService(
        WorkspaceAiPermissionBroker broker,
        WorkspaceAiAuditService audit,
        TextDocumentService? documentService = null,
        TimeProvider? timeProvider = null,
        WorkspaceFileSystemProviderRegistry? workspaceProviders = null,
        HttpClient? networkClient = null)
    {
        _broker = broker;
        _audit = audit;
        _documentService = documentService ?? new TextDocumentService();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _workspaceProviders = workspaceProviders;
        _networkClient = networkClient ?? DefaultNetworkClient;
    }

    public IDisposable BeginAuditScope(string? conversationId)
    {
        var previous = _auditConversationId.Value;
        _auditConversationId.Value = string.IsNullOrWhiteSpace(conversationId)
            ? string.Empty
            : conversationId.Trim();
        return new AuditScope(() => _auditConversationId.Value = previous);
    }

    public async Task<WorkspaceAiOperationResult<T>> ReadVisualPageAsync<T>(
        string workspaceId,
        string pageDocumentPath,
        Func<CancellationToken, Task<T>> read,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        return await ExecuteVisualPagePathOperationAsync(
            workspaceId,
            pageDocumentPath,
            WorkspaceAiCapability.Read,
            WorkspaceAiOperationKind.ReadVisualPage,
            "Read the active visual-page model without DOM or WebView access.",
            read,
            cancellationToken);
    }

    public async Task<WorkspaceAiOperationResult<T>> ValidateVisualPageAsync<T>(
        string workspaceId,
        string pageDocumentPath,
        Func<CancellationToken, Task<T>> validate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validate);
        return await ExecuteVisualPagePathOperationAsync(
            workspaceId,
            pageDocumentPath,
            WorkspaceAiCapability.Read,
            WorkspaceAiOperationKind.ValidateVisualPage,
            "Validated the active visual-page model with the host-owned schema validator.",
            validate,
            cancellationToken);
    }

    public async Task<WorkspaceAiOperationResult<T>> MutateVisualPageAsync<T>(
        string workspaceId,
        string pageDocumentPath,
        Func<CancellationToken, Task<T>> mutate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        return await ExecuteVisualPagePathOperationAsync(
            workspaceId,
            pageDocumentPath,
            WorkspaceAiCapability.Write,
            WorkspaceAiOperationKind.MutateVisualPage,
            "Applied a revision-checked semantic visual-page mutation set.",
            mutate,
            cancellationToken);
    }

    public async Task<WorkspaceAiOperationResult<T>>
        PreviewVisualPageMutationAsync<T>(
            string workspaceId,
            string pageDocumentPath,
            Func<CancellationToken, Task<T>> preview,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return await ExecuteVisualPagePathOperationAsync(
            workspaceId,
            pageDocumentPath,
            WorkspaceAiCapability.Write,
            WorkspaceAiOperationKind.ProposeVisualPageMutation,
            "Validated a semantic visual-page proposal without changing page state.",
            preview,
            cancellationToken);
    }

    public async Task<WorkspaceAiOperationResult<T>>
        PreviewVisualPageTemplateAsync<T>(
            string workspaceId,
            string pageDocumentPath,
            string templatePath,
            Func<CancellationToken, Task<T>> preview,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return await ExecuteVisualPageTemplateOperationAsync(
            workspaceId,
            pageDocumentPath,
            templatePath,
            WorkspaceAiOperationKind.ProposeVisualPageTemplate,
            WorkspaceAiOperationKind.ProposeVisualPageMutation,
            "Validated an inert website-structure template proposal without creating a file.",
            preview,
            cancellationToken);
    }

    public async Task<WorkspaceAiOperationResult<T>>
        CreateVisualPageTemplateAsync<T>(
            string workspaceId,
            string pageDocumentPath,
            string templatePath,
            Func<CancellationToken, Task<T>> create,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        return await ExecuteVisualPageTemplateOperationAsync(
            workspaceId,
            pageDocumentPath,
            templatePath,
            WorkspaceAiOperationKind.CreateVisualPageTemplate,
            WorkspaceAiOperationKind.MutateVisualPage,
            "Created a reviewed inert website-structure template and registered it with the active Hyperpage project.",
            create,
            cancellationToken);
    }

    public WorkspaceAiOperationResult<bool> PrepareVisualPagePreviewBuild(
        string workspaceId,
        string projectRoot)
    {
        var authorization = _broker.AuthorizeTerminal(
            workspaceId,
            execute: true,
            userConfirmed: false,
            projectRoot,
            WorkspaceAiOperationKind.BuildVisualPagePreview);
        if (authorization.Outcome == WorkspaceAiAuthorizationOutcome.Denied)
        {
            return Rejected<bool>(authorization);
        }

        if (authorization.Outcome
            == WorkspaceAiAuthorizationOutcome.ConfirmationRequired)
        {
            TryRecordDecision(authorization, authorization.RelativePath);
            return Success(
                true,
                "The compiled preview build can proceed only after the user reviews and confirms its ambient-code execution warning.");
        }

        if (!TryRecordAuthorization(
                authorization,
                out var auditFailure,
                authorization.RelativePath))
        {
            return Failed<bool>(auditFailure);
        }

        RecordCompletion(
            authorization,
            WorkspaceAiAuditOutcome.Succeeded,
            "Prepared a reviewed isolated visual-page preview build; no process was started.",
            authorization.RelativePath);
        return Success(
            true,
            "The compiled preview build is eligible for explicit review.");
    }

    public async Task<WorkspaceAiOperationResult<T>>
        BuildVisualPagePreviewAsync<T>(
            string workspaceId,
            string projectRoot,
            bool userConfirmed,
            Func<CancellationToken, Task<T>> build,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        cancellationToken.ThrowIfCancellationRequested();
        var authorization = _broker.AuthorizeTerminal(
            workspaceId,
            execute: true,
            userConfirmed,
            projectRoot,
            WorkspaceAiOperationKind.BuildVisualPagePreview);
        if (!authorization.IsAllowed)
        {
            return Rejected<T>(authorization);
        }

        if (!TryRecordAuthorization(
                authorization,
                out var auditFailure,
                authorization.RelativePath))
        {
            return Failed<T>(auditFailure);
        }

        try
        {
            var value = await build(cancellationToken);
            RecordCompletion(
                authorization,
                WorkspaceAiAuditOutcome.Succeeded,
                "Built an isolated latest-revision Hyperpage Studio preview snapshot.",
                authorization.RelativePath);
            return Success(value, "The isolated visual-page preview build completed.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsVisualPageOperationException(exception))
        {
            return FailAfterAuthorization<T>(
                authorization,
                $"The visual-page preview build failed: {exception.Message}");
        }
    }

    private async Task<WorkspaceAiOperationResult<T>>
        ExecuteVisualPagePathOperationAsync<T>(
            string workspaceId,
            string pageDocumentPath,
            WorkspaceAiCapability capability,
            WorkspaceAiOperationKind operation,
            string completionDetail,
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authorization = _broker.AuthorizePath(
            workspaceId,
            pageDocumentPath,
            capability,
            operation);
        if (!authorization.IsAllowed)
        {
            return Rejected<T>(authorization);
        }

        if (!TryRecordAuthorization(
                authorization,
                out var auditFailure,
                authorization.RelativePath))
        {
            return Failed<T>(auditFailure);
        }

        try
        {
            var value = await action(cancellationToken);
            RecordCompletion(
                authorization,
                WorkspaceAiAuditOutcome.Succeeded,
                completionDetail,
                authorization.RelativePath);
            return Success(value, completionDetail);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsVisualPageOperationException(exception))
        {
            return FailAfterAuthorization<T>(authorization, exception.Message);
        }
    }

    private async Task<WorkspaceAiOperationResult<T>>
        ExecuteVisualPageTemplateOperationAsync<T>(
            string workspaceId,
            string pageDocumentPath,
            string templatePath,
            WorkspaceAiOperationKind templateOperation,
            WorkspaceAiOperationKind projectOperation,
            string completionDetail,
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var templateAuthorization = _broker.AuthorizePath(
            workspaceId,
            templatePath,
            WorkspaceAiCapability.Write,
            templateOperation);
        if (!templateAuthorization.IsAllowed)
        {
            return Rejected<T>(templateAuthorization);
        }
        if (templateAuthorization.IsRemote)
        {
            return Failed<T>(
                "Website-derived Hyperpage templates can be created only in the active local project.");
        }
        if (File.Exists(templateAuthorization.FullPath)
            || Directory.Exists(templateAuthorization.FullPath))
        {
            return Failed<T>(
                "The proposed template target already exists; no existing file can be replaced by website capture.");
        }

        var projectAuthorization = _broker.AuthorizePath(
            workspaceId,
            pageDocumentPath,
            WorkspaceAiCapability.Write,
            projectOperation);
        if (!projectAuthorization.IsAllowed)
        {
            return Rejected<T>(projectAuthorization);
        }
        if (!TryRecordAuthorization(
                templateAuthorization,
                out var templateAuditFailure,
                templateAuthorization.RelativePath))
        {
            return Failed<T>(templateAuditFailure);
        }
        if (!TryRecordAuthorization(
                projectAuthorization,
                out var projectAuditFailure,
                projectAuthorization.RelativePath))
        {
            RecordCompletion(
                templateAuthorization,
                WorkspaceAiAuditOutcome.Failed,
                "Project-edit authorization could not be recorded; no template file was created.",
                templateAuthorization.RelativePath);
            return Failed<T>(projectAuditFailure);
        }

        try
        {
            var value = await action(cancellationToken);
            RecordCompletion(
                templateAuthorization,
                WorkspaceAiAuditOutcome.Succeeded,
                completionDetail,
                templateAuthorization.RelativePath);
            RecordCompletion(
                projectAuthorization,
                WorkspaceAiAuditOutcome.Succeeded,
                completionDetail,
                projectAuthorization.RelativePath);
            return Success(value, completionDetail);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsVisualPageOperationException(exception))
        {
            RecordCompletion(
                templateAuthorization,
                WorkspaceAiAuditOutcome.Failed,
                exception.Message,
                templateAuthorization.RelativePath);
            RecordCompletion(
                projectAuthorization,
                WorkspaceAiAuditOutcome.Failed,
                exception.Message,
                projectAuthorization.RelativePath);
            return Failed<T>(exception.Message);
        }
    }

    public async Task<WorkspaceAiOperationResult<WorkspaceAiTextTargetState>>
        InspectTextTargetAsync(
            string workspaceId,
            string target,
            CancellationToken cancellationToken = default)
    {
        var authorization = _broker.AuthorizePath(
            workspaceId,
            target,
            WorkspaceAiCapability.Write,
            WorkspaceAiOperationKind.WriteText);
        if (!authorization.IsAllowed)
        {
            return Rejected<WorkspaceAiTextTargetState>(authorization);
        }

        try
        {
            bool exists;
            if (authorization.IsRemote)
            {
                var (provider, location) = ResolveRemote(authorization);
                var parent = provider.GetParent(location);
                var parentEntry = parent is null
                    ? null
                    : await provider.StatAsync(parent, cancellationToken);
                if (parentEntry is not { IsDirectory: true })
                {
                    return Failed<WorkspaceAiTextTargetState>(
                        "The target parent directory must already exist inside the remote workspace.");
                }

                var entry = await provider.StatAsync(
                    location,
                    cancellationToken);
                if (entry is { IsDirectory: true })
                {
                    return Failed<WorkspaceAiTextTargetState>(
                        "The target is a directory, not a text file.");
                }

                exists = entry is not null;
            }
            else
            {
                var parent = Path.GetDirectoryName(authorization.FullPath);
                if (string.IsNullOrWhiteSpace(parent)
                    || !Directory.Exists(parent))
                {
                    return Failed<WorkspaceAiTextTargetState>(
                        "The target parent directory must already exist inside the approved workspace.");
                }

                if (Directory.Exists(authorization.FullPath))
                {
                    return Failed<WorkspaceAiTextTargetState>(
                        "The target is a directory, not a text file.");
                }

                exists = File.Exists(authorization.FullPath);
            }

            if (exists && !authorization.AllowsEdit)
            {
                return Failed<WorkspaceAiTextTargetState>(
                    "The existing target is outside this context's Edit scope.");
            }

            if (!exists && !authorization.AllowsCreate)
            {
                return Failed<WorkspaceAiTextTargetState>(
                    "The new target is outside this context's Create scope.");
            }

            if (!exists)
            {
                return Success(
                    new WorkspaceAiTextTargetState(
                        authorization.RelativePath,
                        Exists: false,
                        CurrentContent: string.Empty,
                        CurrentSha256: null,
                        ByteLength: 0),
                    "The text target is available for creation.");
            }

            var current = await ReadTextAsync(
                workspaceId,
                authorization.RelativePath,
                cancellationToken);
            return current.Succeeded && current.Value is not null
                ? Success(
                    new WorkspaceAiTextTargetState(
                        authorization.RelativePath,
                        Exists: true,
                        current.Value.Content,
                        current.Value.Sha256,
                        current.Value.ByteLength),
                    "Read the current text target.")
                : Failed<WorkspaceAiTextTargetState>(current.Message);
        }
        catch (Exception exception) when (IsFileOperationException(exception))
        {
            return Failed<WorkspaceAiTextTargetState>(
                $"The text target could not be inspected: {exception.Message}");
        }
    }

    public async Task<WorkspaceAiOperationResult<WorkspaceAiDirectoryListing>>
        ListDirectoryAsync(
            string workspaceId,
            string relativePath = ".",
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authorization = _broker.AuthorizePath(
            workspaceId,
            relativePath,
            WorkspaceAiCapability.Read,
            WorkspaceAiOperationKind.ListDirectory,
            allowWorkspaceRoot: true);
        if (!authorization.IsAllowed)
        {
            return Rejected<WorkspaceAiDirectoryListing>(authorization);
        }

        if (!TryRecordAuthorization(authorization, out var auditFailure))
        {
            return Failed<WorkspaceAiDirectoryListing>(auditFailure);
        }

        try
        {
            if (authorization.IsRemote)
            {
                var (provider, location) = ResolveRemote(authorization);
                var remoteEntries = await provider.ListAsync(
                    location,
                    cancellationToken);
                var remoteTruncated = remoteEntries.Count > MaximumDirectoryEntries;
                var remoteListingEntries = remoteEntries
                    .Where(entry => !entry.IsSymbolicLink)
                    .Take(MaximumDirectoryEntries)
                    .Select(entry => new WorkspaceAiDirectoryEntry(
                        entry.Name,
                        entry.Location.Identity,
                        entry.IsDirectory,
                        entry.IsDirectory ? 0 : entry.Length,
                        entry.LastWriteTimeUtc))
                    .ToList();
                var remoteListing = new WorkspaceAiDirectoryListing(
                    authorization.RelativePath,
                    remoteListingEntries,
                    remoteTruncated);
                RecordCompletion(
                    authorization,
                    WorkspaceAiAuditOutcome.Succeeded,
                    remoteTruncated
                        ? $"Returned {remoteListingEntries.Count} remote entries; the result was truncated."
                        : $"Returned {remoteListingEntries.Count} remote entries.");
                return Success(
                    remoteListing,
                    remoteTruncated
                        ? $"Returned the first {remoteListingEntries.Count} remote entries."
                        : $"Returned {remoteListingEntries.Count} remote workspace entries.");
            }

            if (!Directory.Exists(authorization.FullPath))
            {
                return FailAfterAuthorization<WorkspaceAiDirectoryListing>(
                    authorization,
                    "The requested workspace directory does not exist.");
            }

            var paths = Directory
                .EnumerateFileSystemEntries(authorization.FullPath)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .Take(MaximumDirectoryEntries + 1)
                .ToList();
            var truncated = paths.Count > MaximumDirectoryEntries;
            var entries = paths
                .Take(MaximumDirectoryEntries)
                .Where(path =>
                    (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                .Select(path =>
                {
                    var isDirectory = Directory.Exists(path);
                    var info = isDirectory
                        ? (FileSystemInfo)new DirectoryInfo(path)
                        : new FileInfo(path);
                    return new WorkspaceAiDirectoryEntry(
                        info.Name,
                        Path.IsPathFullyQualified(authorization.RelativePath)
                            ? info.FullName
                            : Path.GetRelativePath(
                                authorization.RootPath,
                                info.FullName),
                        isDirectory,
                        isDirectory ? 0 : ((FileInfo)info).Length,
                        info.LastWriteTimeUtc);
                })
                .ToList();
            var listing = new WorkspaceAiDirectoryListing(
                authorization.RelativePath,
                entries,
                truncated);
            RecordCompletion(
                authorization,
                WorkspaceAiAuditOutcome.Succeeded,
                truncated
                    ? $"Returned {entries.Count} entries; the result was truncated."
                    : $"Returned {entries.Count} entries.");
            return Success(
                listing,
                truncated
                    ? $"Returned the first {entries.Count} entries."
                    : $"Returned {entries.Count} workspace entries.");
        }
        catch (Exception exception) when (IsFileOperationException(exception))
        {
            return FailAfterAuthorization<WorkspaceAiDirectoryListing>(
                authorization,
                $"The directory could not be listed: {exception.Message}");
        }
    }

    public async Task<WorkspaceAiOperationResult<WorkspaceAiTextFile>>
        ReadTextAsync(
            string workspaceId,
            string relativePath,
            CancellationToken cancellationToken = default)
    {
        var authorization = _broker.AuthorizePath(
            workspaceId,
            relativePath,
            WorkspaceAiCapability.Read,
            WorkspaceAiOperationKind.ReadText);
        if (!authorization.IsAllowed)
        {
            return Rejected<WorkspaceAiTextFile>(authorization);
        }

        if (!TryRecordAuthorization(authorization, out var auditFailure))
        {
            return Failed<WorkspaceAiTextFile>(auditFailure);
        }

        byte[]? bytes = null;
        byte[]? currentBytes = null;
        try
        {
            if (authorization.IsRemote)
            {
                var (provider, location) = ResolveRemote(authorization);
                using var remote = await provider.ReadFileAsync(
                    location,
                    MaximumTextBytes,
                    cancellationToken);
                var remoteSnapshot = TextDocumentService.Decode(remote.Content);
                var remoteResult = new WorkspaceAiTextFile(
                    authorization.RelativePath,
                    remoteSnapshot.Text,
                    EditorLanguageCatalog.ResolveLanguageId(location.Path),
                    remoteSnapshot.EncodingKind,
                    remoteSnapshot.LineEndingKind,
                    Hash(remote.Content),
                    remote.Content.LongLength);
                RecordCompletion(
                    authorization,
                    WorkspaceAiAuditOutcome.Succeeded,
                    $"Read {remoteResult.ByteLength} remote byte(s). Content was not recorded.");
                return Success(remoteResult, "Read the remote workspace text file.");
            }

            bytes = await ReadBoundedBytesAsync(
                authorization.FullPath,
                cancellationToken);
            var snapshot = await _documentService.LoadAsync(
                authorization.FullPath,
                cancellationToken);
            currentBytes = await ReadBoundedBytesAsync(
                authorization.FullPath,
                cancellationToken);
            if (!bytes.AsSpan().SequenceEqual(currentBytes))
            {
                return FailAfterAuthorization<WorkspaceAiTextFile>(
                    authorization,
                    "The file changed while it was being read; retry with the current version.");
            }

            var result = new WorkspaceAiTextFile(
                authorization.RelativePath,
                snapshot.Text,
                EditorLanguageCatalog.ResolveLanguageId(
                    authorization.FullPath),
                snapshot.EncodingKind,
                snapshot.LineEndingKind,
                Hash(bytes),
                bytes.LongLength);
            RecordCompletion(
                authorization,
                WorkspaceAiAuditOutcome.Succeeded,
                $"Read {result.ByteLength} byte(s). Content was not recorded.");
            return Success(result, "Read the workspace text file.");
        }
        catch (Exception exception) when (IsFileOperationException(exception))
        {
            return FailAfterAuthorization<WorkspaceAiTextFile>(
                authorization,
                $"The text file could not be read: {exception.Message}");
        }
        finally
        {
            if (bytes is not null)
            {
                CryptographicOperations.ZeroMemory(bytes);
            }

            if (currentBytes is not null)
            {
                CryptographicOperations.ZeroMemory(currentBytes);
            }
        }
    }

    public async Task<WorkspaceAiOperationResult<WorkspaceAiTextSearchResult>>
        SearchTextAsync(
            string workspaceId,
            string root,
            string query,
            string filePattern,
            bool useRegex,
            bool caseSensitive,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(query)
            || query.Length > 512
            || query.IndexOf('\0') >= 0)
        {
            return Failed<WorkspaceAiTextSearchResult>(
                "The search query is empty, malformed, or exceeds 512 characters.");
        }

        if (!TryParseSearchPatterns(filePattern, out var patterns))
        {
            return Failed<WorkspaceAiTextSearchResult>(
                "file_pattern must contain one to sixteen semicolon-separated simple patterns such as *.cs;*.xaml.");
        }

        Regex? regex = null;
        if (useRegex)
        {
            try
            {
                regex = new Regex(
                    query,
                    RegexOptions.CultureInvariant
                    | (caseSensitive
                        ? RegexOptions.None
                        : RegexOptions.IgnoreCase),
                    TimeSpan.FromMilliseconds(200));
            }
            catch (ArgumentException exception)
            {
                return Failed<WorkspaceAiTextSearchResult>(
                    $"The regular expression is invalid: {exception.Message}");
            }
        }

        var authorization = _broker.AuthorizePath(
            workspaceId,
            root,
            WorkspaceAiCapability.Read,
            WorkspaceAiOperationKind.SearchText,
            allowWorkspaceRoot: true);
        if (!authorization.IsAllowed)
        {
            return Rejected<WorkspaceAiTextSearchResult>(authorization);
        }

        if (!TryRecordAuthorization(authorization, out var auditFailure))
        {
            return Failed<WorkspaceAiTextSearchResult>(auditFailure);
        }

        var accumulator = new SearchAccumulator(
            authorization.IsRemote
                ? authorization.WorkspaceUri
                : authorization.FullPath,
            query,
            string.Join(';', patterns));
        try
        {
            if (authorization.IsRemote)
            {
                var (provider, location) = ResolveRemote(authorization);
                await SearchRemoteAsync(
                    provider,
                    location,
                    patterns,
                    query,
                    regex,
                    caseSensitive,
                    accumulator,
                    cancellationToken);
            }
            else
            {
                await SearchLocalAsync(
                    authorization.FullPath,
                    patterns,
                    query,
                    regex,
                    caseSensitive,
                    accumulator,
                    cancellationToken);
            }

            var result = accumulator.ToResult();
            RecordCompletion(
                authorization,
                WorkspaceAiAuditOutcome.Succeeded,
                $"Searched {result.FilesScanned} text file(s) and {result.BytesScanned} byte(s); returned {result.Matches.Count} content-free-indexed match preview(s).",
                authorization.RelativePath);
            return Success(
                result,
                result.IsTruncated
                    ? "Repository text search completed with bounded, truncated results."
                    : "Repository text search completed.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            RecordCompletion(
                authorization,
                WorkspaceAiAuditOutcome.Failed,
                "Repository text search was canceled.");
            throw;
        }
        catch (Exception exception) when (IsFileOperationException(exception)
                                         || exception is RegexMatchTimeoutException)
        {
            return FailAfterAuthorization<WorkspaceAiTextSearchResult>(
                authorization,
                $"Repository text search failed: {exception.Message}");
        }
    }

    public Task<WorkspaceAiOperationResult<WorkspaceAiOpenDocumentSnapshot>>
        ReadOpenDocumentAsync(
            string workspaceId,
            string relativePath,
            string sessionId,
            string title,
            string languageId,
            string providerId,
            bool hasUnsavedChanges,
            Func<string> readContent,
            Func<DocumentReadResult> analyze,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readContent);
        ArgumentNullException.ThrowIfNull(analyze);
        cancellationToken.ThrowIfCancellationRequested();
        var authorization = _broker.AuthorizePath(
            workspaceId,
            relativePath,
            WorkspaceAiCapability.Read,
            WorkspaceAiOperationKind.ReadOpenDocument);
        if (!authorization.IsAllowed)
        {
            return Task.FromResult(
                Rejected<WorkspaceAiOpenDocumentSnapshot>(authorization));
        }

        if (!TryRecordAuthorization(authorization, out var auditFailure))
        {
            return Task.FromResult(
                Failed<WorkspaceAiOpenDocumentSnapshot>(auditFailure));
        }

        try
        {
            var content = readContent() ?? string.Empty;
            if (content.IndexOf('\0') >= 0)
            {
                return Task.FromResult(
                    FailAfterAuthorization<WorkspaceAiOpenDocumentSnapshot>(
                        authorization,
                        "The open document contains a null character and cannot be shared with the AI provider."));
            }

            var byteLength = Encoding.UTF8.GetByteCount(content);
            if (byteLength > MaximumTextBytes)
            {
                return Task.FromResult(
                    FailAfterAuthorization<WorkspaceAiOpenDocumentSnapshot>(
                        authorization,
                        $"The open document exceeds the {MaximumTextBytes / 1024 / 1024} MiB AI read limit."));
            }

            var analysis = analyze();
            var snapshot = new WorkspaceAiOpenDocumentSnapshot(
                sessionId,
                title,
                authorization.RelativePath,
                content,
                languageId,
                providerId,
                analysis.Summary,
                analysis.Diagnostics.Take(64).ToList(),
                HashSensitiveText(content),
                hasUnsavedChanges);
            RecordCompletion(
                authorization,
                WorkspaceAiAuditOutcome.Succeeded,
                $"Read {byteLength} live editor byte(s) through provider {providerId}; content was not recorded.");
            return Task.FromResult(Success(
                snapshot,
                "Read the live open document through its typed provider."));
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                         or ArgumentException
                                         or NotSupportedException)
        {
            return Task.FromResult(
                FailAfterAuthorization<WorkspaceAiOpenDocumentSnapshot>(
                    authorization,
                    $"The open document could not be read through its typed provider: {exception.Message}"));
        }
    }

    public async Task<WorkspaceAiOperationResult<WorkspaceAiOpenDocumentMutationReceipt>>
        ReplaceOpenDocumentAsync(
            string workspaceId,
            string relativePath,
            string sessionId,
            string replacement,
            string expectedSha256,
            Func<string> readCurrentContent,
            Func<string, DocumentMutationResult> replace,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readCurrentContent);
        ArgumentNullException.ThrowIfNull(replace);
        var authorization = _broker.AuthorizePath(
            workspaceId,
            relativePath,
            WorkspaceAiCapability.Write,
            WorkspaceAiOperationKind.ReplaceOpenDocument);
        if (!authorization.IsAllowed)
        {
            return Rejected<WorkspaceAiOpenDocumentMutationReceipt>(
                authorization);
        }

        if (!IsSha256(expectedSha256))
        {
            return FailAfterAuthorization<WorkspaceAiOpenDocumentMutationReceipt>(
                authorization,
                "Replacing an open document requires its current SHA-256 precondition.");
        }

        if (replacement.IndexOf('\0') >= 0
            || Encoding.UTF8.GetByteCount(replacement) > MaximumTextBytes)
        {
            return FailAfterAuthorization<WorkspaceAiOpenDocumentMutationReceipt>(
                authorization,
                $"The proposed editor replacement is malformed or exceeds the {MaximumTextBytes / 1024 / 1024} MiB AI write limit.");
        }

        if (!TryRecordAuthorization(authorization, out var auditFailure))
        {
            return Failed<WorkspaceAiOpenDocumentMutationReceipt>(
                auditFailure);
        }

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var currentContent = readCurrentContent() ?? string.Empty;
            if (!string.Equals(
                    HashSensitiveText(currentContent),
                    expectedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return FailAfterAuthorization<WorkspaceAiOpenDocumentMutationReceipt>(
                    authorization,
                    "The open document changed after it was reviewed; the stale replacement was rejected.");
            }

            var mutation = replace(replacement);
            if (!mutation.Succeeded)
            {
                var diagnostic = mutation.Diagnostics.FirstOrDefault(
                    item => item.Severity
                        == DocumentProviderDiagnosticSeverity.Error);
                return FailAfterAuthorization<WorkspaceAiOpenDocumentMutationReceipt>(
                    authorization,
                    diagnostic is null
                        ? mutation.Message
                        : $"{mutation.Message} {diagnostic.Code}: {diagnostic.Message}");
            }

            var appliedContent = readCurrentContent() ?? string.Empty;
            var receipt = new WorkspaceAiOpenDocumentMutationReceipt(
                sessionId,
                authorization.RelativePath,
                HashSensitiveText(appliedContent),
                appliedContent.Length,
                mutation.Message);
            RecordCompletion(
                authorization,
                WorkspaceAiAuditOutcome.Succeeded,
                $"Replaced live editor content through its typed provider; {receipt.CharacterLength} character(s), content not recorded.");
            return Success(
                receipt,
                "Applied the reviewed replacement to the open document. The document remains unsaved until the user saves it.");
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                         or ArgumentException
                                         or NotSupportedException)
        {
            return FailAfterAuthorization<WorkspaceAiOpenDocumentMutationReceipt>(
                authorization,
                $"The reviewed replacement could not be applied through the typed provider: {exception.Message}");
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<WorkspaceAiOperationResult<WorkspaceAiWriteReceipt>>
        WriteTextAsync(
            string workspaceId,
            string relativePath,
            string content,
            string? expectedSha256,
            bool allowCreate,
            CancellationToken cancellationToken = default)
    {
        var authorization = _broker.AuthorizePath(
            workspaceId,
            relativePath,
            WorkspaceAiCapability.Write,
            WorkspaceAiOperationKind.WriteText);
        if (!authorization.IsAllowed)
        {
            return Rejected<WorkspaceAiWriteReceipt>(authorization);
        }

        if (content.IndexOf('\0') >= 0)
        {
            return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                authorization,
                "Text writes cannot contain a null character.");
        }

        if (Encoding.UTF8.GetByteCount(content) > MaximumTextBytes)
        {
            return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                authorization,
                $"The proposed text exceeds the {MaximumTextBytes / 1024 / 1024} MiB AI write limit.");
        }

        if (!TryRecordAuthorization(authorization, out var auditFailure))
        {
            return Failed<WorkspaceAiWriteReceipt>(auditFailure);
        }

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            if (authorization.IsRemote)
            {
                return await WriteRemoteTextAsync(
                    authorization,
                    content,
                    expectedSha256,
                    allowCreate,
                    cancellationToken);
            }

            var parent = Path.GetDirectoryName(authorization.FullPath);
            if (string.IsNullOrWhiteSpace(parent)
                || !Directory.Exists(parent))
            {
                return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                    authorization,
                    "The target parent directory must already exist inside the workspace.");
            }

            var existed = File.Exists(authorization.FullPath);
            if (existed && !authorization.AllowsEdit)
            {
                return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                    authorization,
                    "The existing target is outside this context's Edit scope.");
            }

            if (!existed && !authorization.AllowsCreate)
            {
                return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                    authorization,
                    "The new target is outside this context's Create scope.");
            }

            DocumentEncodingKind encodingKind;
            byte[]? originalBytes = null;
            if (existed)
            {
                if (!IsSha256(expectedSha256))
                {
                    return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                        authorization,
                        "Replacing an existing file requires its current SHA-256 precondition.");
                }

                originalBytes = await ReadBoundedBytesAsync(
                    authorization.FullPath,
                    cancellationToken);
                if (!string.Equals(
                        Hash(originalBytes),
                        expectedSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    CryptographicOperations.ZeroMemory(originalBytes);
                    return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                        authorization,
                        "The file changed after it was read; the stale write was rejected.");
                }

                var snapshot = await _documentService.LoadAsync(
                    authorization.FullPath,
                    cancellationToken);
                encodingKind = snapshot.EncodingKind;
            }
            else
            {
                if (!allowCreate)
                {
                    return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                        authorization,
                        "Creating a new file was not authorized for this operation.");
                }

                if (!string.IsNullOrWhiteSpace(expectedSha256))
                {
                    return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                        authorization,
                        "A new file cannot use an existing-content SHA-256 precondition.");
                }

                encodingKind = DocumentEncodingKind.Utf8;
            }

            var temporaryPath = Path.Combine(
                parent,
                $".{Path.GetFileName(authorization.FullPath)}.{Guid.NewGuid():N}.ai-write");
            try
            {
                await _documentService.SaveAsync(
                    temporaryPath,
                    content,
                    encodingKind,
                    cancellationToken);
                var encodedLength = new FileInfo(temporaryPath).Length;
                if (encodedLength > MaximumTextBytes)
                {
                    return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                        authorization,
                        $"The encoded text exceeds the {MaximumTextBytes / 1024 / 1024} MiB AI write limit.");
                }

                if (existed)
                {
                    var latestBytes = await ReadBoundedBytesAsync(
                        authorization.FullPath,
                        cancellationToken);
                    var unchanged = originalBytes is not null
                                    && originalBytes.AsSpan().SequenceEqual(
                                        latestBytes);
                    CryptographicOperations.ZeroMemory(latestBytes);
                    if (!unchanged)
                    {
                        return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                            authorization,
                            "The file changed while the replacement was prepared; no write was applied.");
                    }
                }
                else if (File.Exists(authorization.FullPath))
                {
                    return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                        authorization,
                        "Another process created the file first; no overwrite was attempted.");
                }

                File.Move(
                    temporaryPath,
                    authorization.FullPath,
                    overwrite: existed);
                var writtenBytes = await ReadBoundedBytesAsync(
                    authorization.FullPath,
                    cancellationToken);
                var receipt = new WorkspaceAiWriteReceipt(
                    authorization.RelativePath,
                    Hash(writtenBytes),
                    writtenBytes.LongLength,
                    WasCreated: !existed);
                CryptographicOperations.ZeroMemory(writtenBytes);
                RecordCompletion(
                    authorization,
                    WorkspaceAiAuditOutcome.Succeeded,
                    $"{(receipt.WasCreated ? "Created" : "Replaced")} {receipt.ByteLength} byte(s). Content was not recorded.");
                return Success(
                    receipt,
                    receipt.WasCreated
                        ? "Created the workspace text file atomically."
                        : "Replaced the workspace text file atomically after verifying its SHA-256 precondition.");
            }
            finally
            {
                if (originalBytes is not null)
                {
                    CryptographicOperations.ZeroMemory(originalBytes);
                }

                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (Exception exception) when (IsFileOperationException(exception))
        {
            return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                authorization,
                $"The workspace text file could not be written: {exception.Message}");
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<WorkspaceAiOperationResult<WorkspaceAiDeleteReceipt>>
        DeleteTextAsync(
            string workspaceId,
            string path,
            string expectedSha256,
            CancellationToken cancellationToken = default)
    {
        var authorization = _broker.AuthorizePath(
            workspaceId,
            path,
            WorkspaceAiCapability.Delete,
            WorkspaceAiOperationKind.DeleteText);
        if (!authorization.IsAllowed)
        {
            return Rejected<WorkspaceAiDeleteReceipt>(authorization);
        }

        if (!IsSha256(expectedSha256))
        {
            return FailAfterAuthorization<WorkspaceAiDeleteReceipt>(
                authorization,
                "Deleting a text file requires its current SHA-256 precondition.");
        }

        if (!TryRecordAuthorization(authorization, out var auditFailure))
        {
            return Failed<WorkspaceAiDeleteReceipt>(auditFailure);
        }

        await _writeGate.WaitAsync(cancellationToken);
        byte[]? bytes = null;
        try
        {
            if (authorization.IsRemote)
            {
                return await DeleteRemoteTextAsync(
                    authorization,
                    expectedSha256,
                    cancellationToken);
            }

            if (!File.Exists(authorization.FullPath)
                || Directory.Exists(authorization.FullPath))
            {
                return FailAfterAuthorization<WorkspaceAiDeleteReceipt>(
                    authorization,
                    "The text file no longer exists.");
            }

            bytes = await ReadBoundedBytesAsync(
                authorization.FullPath,
                cancellationToken);
            var actualSha256 = Hash(bytes);
            if (!string.Equals(
                    actualSha256,
                    expectedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return FailAfterAuthorization<WorkspaceAiDeleteReceipt>(
                    authorization,
                    "The file changed after it was read; the stale delete was rejected.");
            }

            var receipt = new WorkspaceAiDeleteReceipt(
                authorization.RelativePath,
                actualSha256,
                bytes.LongLength);
            File.Delete(authorization.FullPath);
            RecordCompletion(
                authorization,
                WorkspaceAiAuditOutcome.Succeeded,
                $"Permanently deleted {receipt.ByteLength} byte(s) after verifying the SHA-256 precondition; content was not recorded.");
            return Success(
                receipt,
                "Permanently deleted the text file after verifying its current SHA-256.");
        }
        catch (Exception exception) when (IsFileOperationException(exception))
        {
            return FailAfterAuthorization<WorkspaceAiDeleteReceipt>(
                authorization,
                $"The text file could not be deleted: {exception.Message}");
        }
        finally
        {
            if (bytes is not null)
            {
                CryptographicOperations.ZeroMemory(bytes);
            }

            _writeGate.Release();
        }
    }

    private async Task<WorkspaceAiOperationResult<WorkspaceAiWriteReceipt>>
        WriteRemoteTextAsync(
            WorkspaceAiAuthorization authorization,
            string content,
            string? expectedSha256,
            bool allowCreate,
            CancellationToken cancellationToken)
    {
        var (provider, location) = ResolveRemote(authorization);
        var parent = provider.GetParent(location);
        var parentEntry = parent is null
            ? null
            : await provider.StatAsync(parent, cancellationToken);
        if (parentEntry is not { IsDirectory: true })
        {
            return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                authorization,
                "The target parent directory does not exist on the linked device.");
        }

        var entry = await provider.StatAsync(location, cancellationToken);
        if (entry is { IsDirectory: true })
        {
            return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                authorization,
                "The remote target is a directory, not a text file.");
        }

        var existed = entry is not null;
        if (existed && !authorization.AllowsEdit)
        {
            return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                authorization,
                "The existing remote target is outside this context's Edit scope.");
        }

        if (!existed && !authorization.AllowsCreate)
        {
            return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                authorization,
                "The new remote target is outside this context's Create scope.");
        }

        DocumentEncodingKind encodingKind;
        string expectedVersion = string.Empty;
        if (existed)
        {
            if (!IsSha256(expectedSha256))
            {
                return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                    authorization,
                    "Replacing an existing remote file requires its current SHA-256 precondition.");
            }

            using var current = await provider.ReadFileAsync(
                location,
                MaximumTextBytes,
                cancellationToken);
            if (!string.Equals(
                    Hash(current.Content),
                    expectedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                    authorization,
                    "The remote file changed after it was read; the stale write was rejected.");
            }

            encodingKind = TextDocumentService.Decode(current.Content)
                .EncodingKind;
            expectedVersion = current.Version;
        }
        else
        {
            if (!allowCreate)
            {
                return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                    authorization,
                    "Creating the remote file was not authorized for this operation.");
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                    authorization,
                    "A new remote file cannot use an existing-content SHA-256 precondition.");
            }

            encodingKind = DocumentEncodingKind.Utf8;
        }

        var encoded = TextDocumentService.Encode(content, encodingKind);
        try
        {
            if (encoded.LongLength > MaximumTextBytes)
            {
                return FailAfterAuthorization<WorkspaceAiWriteReceipt>(
                    authorization,
                    $"The encoded text exceeds the {MaximumTextBytes / 1024 / 1024} MiB AI write limit.");
            }

            if (existed)
            {
                _ = await provider.ReplaceFileAsync(
                    location,
                    encoded,
                    new WorkspaceWriteOptions
                    {
                        ExpectedVersion = expectedVersion
                    },
                    cancellationToken);
            }
            else
            {
                await provider.CreateFileAsync(
                    location,
                    encoded,
                    cancellationToken);
            }

            using var written = await provider.ReadFileAsync(
                location,
                MaximumTextBytes,
                cancellationToken);
            var receipt = new WorkspaceAiWriteReceipt(
                authorization.RelativePath,
                Hash(written.Content),
                written.Content.LongLength,
                WasCreated: !existed);
            RecordCompletion(
                authorization,
                WorkspaceAiAuditOutcome.Succeeded,
                $"{(receipt.WasCreated ? "Created" : "Replaced")} {receipt.ByteLength} remote byte(s). Content was not recorded.");
            return Success(
                receipt,
                receipt.WasCreated
                    ? "Created the remote workspace text file."
                    : "Replaced the remote workspace text file after verifying its current version.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
        }
    }

    private async Task<WorkspaceAiOperationResult<WorkspaceAiDeleteReceipt>>
        DeleteRemoteTextAsync(
            WorkspaceAiAuthorization authorization,
            string expectedSha256,
            CancellationToken cancellationToken)
    {
        var (provider, location) = ResolveRemote(authorization);
        var entry = await provider.StatAsync(location, cancellationToken);
        if (entry is null || entry.IsDirectory)
        {
            return FailAfterAuthorization<WorkspaceAiDeleteReceipt>(
                authorization,
                "The remote text file no longer exists.");
        }

        using var current = await provider.ReadFileAsync(
            location,
            MaximumTextBytes,
            cancellationToken);
        var actualSha256 = Hash(current.Content);
        if (!string.Equals(
                actualSha256,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return FailAfterAuthorization<WorkspaceAiDeleteReceipt>(
                authorization,
                "The remote file changed after it was read; the stale delete was rejected.");
        }

        var receipt = new WorkspaceAiDeleteReceipt(
            authorization.RelativePath,
            actualSha256,
            current.Content.LongLength);
        await provider.DeleteAsync(
            location,
            new WorkspaceDeleteConfirmation
            {
                ConfirmedLocationIdentity = location.Identity,
                ConfirmPermanentDeletion = true,
                Recursive = false
            },
            cancellationToken);
        RecordCompletion(
            authorization,
            WorkspaceAiAuditOutcome.Succeeded,
            $"Permanently deleted {receipt.ByteLength} remote byte(s) after verifying the SHA-256 precondition; content was not recorded.");
        return Success(
            receipt,
            "Permanently deleted the remote text file after verifying its current SHA-256.");
    }

    public async Task<WorkspaceAiOperationResult<WorkspaceAiNetworkTextSnapshot>>
        FetchInternetTextAsync(
            string workspaceId,
            string url,
            CancellationToken cancellationToken = default)
    {
        var authorization = _broker.AuthorizeNetwork(workspaceId);
        if (!authorization.IsAllowed)
        {
            return Rejected<WorkspaceAiNetworkTextSnapshot>(authorization);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            return FailAfterAuthorization<WorkspaceAiNetworkTextSnapshot>(
                authorization,
                "Internet tools accept only public HTTPS URLs without credentials or custom ports.");
        }

        var auditTarget = $"https://{uri.Host}{uri.AbsolutePath}";
        if (!TryRecordAuthorization(
                authorization,
                out var auditFailure,
                auditTarget))
        {
            return Failed<WorkspaceAiNetworkTextSnapshot>(auditFailure);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Hypershell/1.0");
            request.Headers.Accept.ParseAdd(
                "text/plain, text/html, application/json, application/xml;q=0.9, text/*;q=0.8");
            using var response = await _networkClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                return FailAfterAuthorization<WorkspaceAiNetworkTextSnapshot>(
                    authorization,
                    "Internet redirects are not followed. Use the final public HTTPS URL.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return FailAfterAuthorization<WorkspaceAiNetworkTextSnapshot>(
                    authorization,
                    $"The HTTPS request returned status {(int)response.StatusCode}.");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType
                            ?? "text/plain";
            if (!IsTextMediaType(mediaType))
            {
                return FailAfterAuthorization<WorkspaceAiNetworkTextSnapshot>(
                    authorization,
                    $"The HTTPS response type '{mediaType}' is not a supported text format.");
            }

            if (response.Content.Headers.ContentLength
                is > MaximumNetworkTextBytes)
            {
                return FailAfterAuthorization<WorkspaceAiNetworkTextSnapshot>(
                    authorization,
                    "The HTTPS response exceeds the 512 KiB AI internet limit.");
            }

            await using var input = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            using var output = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (output.Length + read > MaximumNetworkTextBytes)
                {
                    return FailAfterAuthorization<WorkspaceAiNetworkTextSnapshot>(
                        authorization,
                        "The HTTPS response exceeds the 512 KiB AI internet limit.");
                }

                output.Write(buffer, 0, read);
            }

            var content = Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
            var snapshot = new WorkspaceAiNetworkTextSnapshot(
                uri.GetLeftPart(UriPartial.Path),
                mediaType,
                content,
                (int)response.StatusCode);
            RecordCompletion(
                authorization,
                WorkspaceAiAuditOutcome.Succeeded,
                $"Read {output.Length} byte(s) from a public HTTPS text response; response content was not recorded.",
                auditTarget);
            return Success(snapshot, "Read the public HTTPS text response.");
        }
        catch (Exception exception) when (exception is HttpRequestException
                                         or IOException
                                         or SocketException
                                         or InvalidOperationException
                                         or NotSupportedException)
        {
            return FailAfterAuthorization<WorkspaceAiNetworkTextSnapshot>(
                authorization,
                $"The public HTTPS text request failed: {exception.Message}");
        }
    }

    public async Task<WorkspaceAiOperationResult<WorkspaceAiTerminalDispatchReceipt>>
        DispatchTerminalAsync(
            string workspaceId,
            string command,
            bool execute,
            bool userConfirmed,
            Func<string, string, bool, CancellationToken, Task> dispatch,
            CancellationToken cancellationToken = default) =>
        await DispatchTerminalAsync(
            workspaceId,
            string.Empty,
            command,
            execute,
            userConfirmed,
            dispatch,
            cancellationToken);

    public async Task<WorkspaceAiOperationResult<WorkspaceAiTerminalDispatchReceipt>>
        DispatchTerminalAsync(
            string workspaceId,
            string terminalTarget,
            string command,
            bool execute,
            bool userConfirmed,
            Func<string, string, bool, CancellationToken, Task> dispatch,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        if (string.IsNullOrWhiteSpace(command)
            || command.Length > MaximumCommandCharacters
            || command.Any(character => char.IsControl(character)
                && character is not '\r' and not '\n' and not '\t'))
        {
            return Failed<WorkspaceAiTerminalDispatchReceipt>(
                "The requested terminal command is empty, malformed, or exceeds the 32 KiB limit.");
        }

        var authorization = _broker.AuthorizeTerminal(
            workspaceId,
            execute,
            userConfirmed,
            terminalTarget);
        var commandSha256 = HashSensitiveText(command);
        var auditTarget = $"command-sha256:{commandSha256[..16]}";
        if (!authorization.IsAllowed)
        {
            TryRecordDecision(authorization, auditTarget);
            return new WorkspaceAiOperationResult<WorkspaceAiTerminalDispatchReceipt>
            {
                Status = authorization.Outcome
                         == WorkspaceAiAuthorizationOutcome.ConfirmationRequired
                    ? WorkspaceAiOperationStatus.ConfirmationRequired
                    : WorkspaceAiOperationStatus.Denied,
                Message = authorization.Message
            };
        }

        if (!TryRecordAuthorization(
                authorization,
                out var auditFailure,
                auditTarget))
        {
            return Failed<WorkspaceAiTerminalDispatchReceipt>(auditFailure);
        }

        try
        {
            await dispatch(
                command,
                authorization.RootPath,
                execute,
                cancellationToken);
            RecordCompletion(
                authorization,
                WorkspaceAiAuditOutcome.Succeeded,
                execute
                    ? "The authorized command completed host dispatch; command text was not recorded."
                    : "The command was staged without execution; command text was not recorded.",
                auditTarget);
            return Success(
                new WorkspaceAiTerminalDispatchReceipt(
                    execute,
                    commandSha256),
                execute
                    ? "The authorized command was dispatched to the terminal."
                    : "The command was staged in the terminal and was not executed.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            RecordCompletion(
                authorization,
                WorkspaceAiAuditOutcome.Failed,
                "Terminal dispatch was canceled; command text was not recorded.",
                auditTarget);
            throw;
        }
        catch (Exception exception)
        {
            RecordCompletion(
                authorization,
                WorkspaceAiAuditOutcome.Failed,
                $"Terminal dispatch failed with {exception.GetType().Name}; command text was not recorded.",
                auditTarget);
            return Failed<WorkspaceAiTerminalDispatchReceipt>(
                "The host could not dispatch the terminal command.");
        }
    }

    public Task<WorkspaceAiOperationResult<WorkspaceAiTerminalStateSnapshot>>
        ReadTerminalStateAsync(
            string workspaceId,
            string title,
            string shellKind,
            string workingDirectory,
            string prompt,
            bool isRunning,
            Func<string> readDraftCommand,
            Func<IReadOnlyList<WorkspaceAiTerminalLineSnapshot>> readTranscript,
            CancellationToken cancellationToken = default) =>
        ReadTerminalStateAsync(
            workspaceId,
            title,
            shellKind,
            workingDirectory,
            string.Empty,
            prompt,
            isRunning,
            readDraftCommand,
            readTranscript,
            cancellationToken);

    public Task<WorkspaceAiOperationResult<WorkspaceAiTerminalStateSnapshot>>
        ReadTerminalStateAsync(
            string workspaceId,
        string title,
        string shellKind,
        string workingDirectory,
        string workspaceUri,
        string prompt,
            bool isRunning,
            Func<string> readDraftCommand,
            Func<IReadOnlyList<WorkspaceAiTerminalLineSnapshot>> readTranscript,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readDraftCommand);
        ArgumentNullException.ThrowIfNull(readTranscript);
        cancellationToken.ThrowIfCancellationRequested();
        var authorization = _broker.AuthorizeTerminalRead(
            workspaceId,
            string.IsNullOrWhiteSpace(workspaceUri)
                ? workingDirectory
                : workspaceUri);
        if (!authorization.IsAllowed)
        {
            return Task.FromResult(
                Rejected<WorkspaceAiTerminalStateSnapshot>(authorization));
        }

        if (!TryRecordAuthorization(authorization, out var auditFailure))
        {
            return Task.FromResult(
                Failed<WorkspaceAiTerminalStateSnapshot>(auditFailure));
        }

        try
        {
            var draft = readDraftCommand() ?? string.Empty;
            if (draft.Length > MaximumCommandCharacters
                || draft.IndexOf('\0') >= 0)
            {
                return Task.FromResult(
                    FailAfterAuthorization<WorkspaceAiTerminalStateSnapshot>(
                        authorization,
                        "The live terminal draft is malformed or exceeds the 32 KiB AI read limit."));
            }

            var sourceLines = readTranscript()
                              ?? Array.Empty<WorkspaceAiTerminalLineSnapshot>();
            var selected = new List<WorkspaceAiTerminalLineSnapshot>();
            var characters = 0;
            var truncated = sourceLines.Count > MaximumTerminalTranscriptLines;
            for (var index = sourceLines.Count - 1;
                 index >= 0 && selected.Count < MaximumTerminalTranscriptLines;
                 index--)
            {
                var line = sourceLines[index];
                if (line.Text.IndexOf('\0') >= 0)
                {
                    truncated = true;
                    continue;
                }

                if (characters + line.Text.Length
                    > MaximumTerminalTranscriptCharacters)
                {
                    truncated = true;
                    break;
                }

                selected.Add(line);
                characters += line.Text.Length;
            }

            selected.Reverse();
            var snapshot = new WorkspaceAiTerminalStateSnapshot(
                title,
                shellKind,
                authorization.FullPath,
                prompt,
                isRunning,
                draft,
                selected,
                truncated);
            RecordCompletion(
                authorization,
                WorkspaceAiAuditOutcome.Succeeded,
                $"Read a live terminal draft and {selected.Count} bounded transcript line(s); content was not recorded.");
            return Task.FromResult(Success(
                snapshot,
                "Read the live terminal draft and bounded transcript."));
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                         or ArgumentException
                                         or NotSupportedException)
        {
            return Task.FromResult(
                FailAfterAuthorization<WorkspaceAiTerminalStateSnapshot>(
                    authorization,
                    $"The live terminal state could not be read: {exception.Message}"));
        }
    }

    private async Task<byte[]> ReadBoundedBytesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "The workspace file does not exist.",
                path);
        }

        if (file.Length > MaximumTextBytes)
        {
            throw new InvalidDataException(
                $"The workspace file exceeds the {MaximumTextBytes / 1024 / 1024} MiB AI read limit.");
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    private async Task SearchLocalAsync(
        string root,
        IReadOnlyList<string> patterns,
        string query,
        Regex? regex,
        bool caseSensitive,
        SearchAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        if (File.Exists(root))
        {
            await SearchLocalFileAsync(
                root,
                Path.GetFileName(root),
                patterns,
                query,
                regex,
                caseSensitive,
                accumulator,
                cancellationToken);
            return;
        }

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                "The repository search root does not exist.");
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0 && !accumulator.ShouldStop)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            IReadOnlyList<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory)
                    .Take(MaximumSearchDirectoryEntries + 1)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception exception) when (IsFileOperationException(exception))
            {
                accumulator.SkipFile();
                continue;
            }

            if (entries.Count > MaximumSearchDirectoryEntries)
            {
                accumulator.Truncate();
                entries = entries.Take(MaximumSearchDirectoryEntries).ToList();
            }

            for (var index = entries.Count - 1;
                 index >= 0 && !accumulator.ShouldStop;
                 index--)
            {
                if (!accumulator.TryVisitEntry())
                {
                    break;
                }

                var entry = entries[index];
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (IsFileOperationException(exception))
                {
                    accumulator.SkipFile();
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    accumulator.SkipFile();
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!SearchExcludedDirectories.Contains(
                            Path.GetFileName(entry)))
                    {
                        pending.Push(entry);
                    }

                    continue;
                }

                var displayPath = Path.GetRelativePath(root, entry);
                await SearchLocalFileAsync(
                    entry,
                    displayPath,
                    patterns,
                    query,
                    regex,
                    caseSensitive,
                    accumulator,
                    cancellationToken);
            }
        }
    }

    private async Task SearchLocalFileAsync(
        string path,
        string displayPath,
        IReadOnlyList<string> patterns,
        string query,
        Regex? regex,
        bool caseSensitive,
        SearchAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        if (!MatchesSearchPattern(path, displayPath, patterns)
            || IsExcludedSearchFile(path))
        {
            return;
        }

        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaximumSearchFileBytes)
        {
            accumulator.SkipFile();
            return;
        }

        if (!accumulator.CanRead(info.Length))
        {
            return;
        }

        byte[]? bytes = null;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            SearchDecodedContent(
                path,
                NormalizeDisplayPath(displayPath),
                bytes,
                query,
                regex,
                caseSensitive,
                accumulator);
        }
        catch (Exception exception) when (IsFileOperationException(exception))
        {
            accumulator.SkipFile();
        }
        finally
        {
            if (bytes is not null)
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }

    private async Task SearchRemoteAsync(
        IWorkspaceFileSystemProvider provider,
        WorkspaceLocation root,
        IReadOnlyList<string> patterns,
        string query,
        Regex? regex,
        bool caseSensitive,
        SearchAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        var rootEntry = await provider.StatAsync(root, cancellationToken)
                        ?? throw new DirectoryNotFoundException(
                            "The remote repository search root does not exist.");
        if (!rootEntry.IsDirectory)
        {
            await SearchRemoteFileAsync(
                provider,
                root,
                rootEntry.Name,
                rootEntry.Length,
                patterns,
                query,
                regex,
                caseSensitive,
                accumulator,
                cancellationToken);
            return;
        }

        var pending = new Stack<WorkspaceLocation>();
        pending.Push(root);
        while (pending.Count > 0 && !accumulator.ShouldStop)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            IReadOnlyList<WorkspaceEntry> entries;
            try
            {
                entries = await provider.ListAsync(directory, cancellationToken);
            }
            catch (Exception exception) when (IsFileOperationException(exception))
            {
                accumulator.SkipFile();
                continue;
            }

            var ordered = entries
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaximumSearchDirectoryEntries + 1)
                .ToList();
            if (ordered.Count > MaximumSearchDirectoryEntries)
            {
                accumulator.Truncate();
                ordered = ordered.Take(MaximumSearchDirectoryEntries).ToList();
            }

            for (var index = ordered.Count - 1;
                 index >= 0 && !accumulator.ShouldStop;
                 index--)
            {
                if (!accumulator.TryVisitEntry())
                {
                    break;
                }

                var entry = ordered[index];
                if (entry.IsSymbolicLink)
                {
                    accumulator.SkipFile();
                    continue;
                }

                if (entry.IsDirectory)
                {
                    if (!SearchExcludedDirectories.Contains(entry.Name))
                    {
                        pending.Push(entry.Location);
                    }

                    continue;
                }

                var displayPath = GetRemoteDisplayPath(root.Path, entry.Location.Path);
                await SearchRemoteFileAsync(
                    provider,
                    entry.Location,
                    displayPath,
                    entry.Length,
                    patterns,
                    query,
                    regex,
                    caseSensitive,
                    accumulator,
                    cancellationToken);
            }
        }
    }

    private async Task SearchRemoteFileAsync(
        IWorkspaceFileSystemProvider provider,
        WorkspaceLocation location,
        string displayPath,
        long length,
        IReadOnlyList<string> patterns,
        string query,
        Regex? regex,
        bool caseSensitive,
        SearchAccumulator accumulator,
        CancellationToken cancellationToken)
    {
        if (!MatchesSearchPattern(location.Path, displayPath, patterns)
            || IsExcludedSearchFile(location.Path))
        {
            return;
        }

        if (length > MaximumSearchFileBytes || !accumulator.CanRead(length))
        {
            accumulator.SkipFile();
            return;
        }

        try
        {
            using var content = await provider.ReadFileAsync(
                location,
                MaximumSearchFileBytes,
                cancellationToken);
            SearchDecodedContent(
                location.Identity,
                NormalizeDisplayPath(displayPath),
                content.Content,
                query,
                regex,
                caseSensitive,
                accumulator);
        }
        catch (Exception exception) when (IsFileOperationException(exception))
        {
            accumulator.SkipFile();
        }
    }

    private static void SearchDecodedContent(
        string path,
        string displayPath,
        byte[] bytes,
        string query,
        Regex? regex,
        bool caseSensitive,
        SearchAccumulator accumulator)
    {
        if (!accumulator.TryBeginFile(bytes.LongLength))
        {
            return;
        }

        var decoded = TextDocumentService.Decode(bytes).Text;
        if (LooksBinary(decoded))
        {
            accumulator.SkipDecodedFile();
            return;
        }

        var lines = decoded
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        for (var lineIndex = 0;
             lineIndex < lines.Length && !accumulator.MatchLimitReached;
             lineIndex++)
        {
            var line = lines[lineIndex];
            if (regex is not null)
            {
                foreach (Match match in regex.Matches(line))
                {
                    accumulator.AddMatch(
                        path,
                        displayPath,
                        lineIndex + 1,
                        match.Index + 1,
                        CreateSearchPreview(line));
                    if (accumulator.MatchLimitReached)
                    {
                        break;
                    }
                }

                continue;
            }

            var start = 0;
            while (start <= line.Length && !accumulator.MatchLimitReached)
            {
                var index = line.IndexOf(query, start, comparison);
                if (index < 0)
                {
                    break;
                }

                accumulator.AddMatch(
                    path,
                    displayPath,
                    lineIndex + 1,
                    index + 1,
                    CreateSearchPreview(line));
                start = index + Math.Max(1, query.Length);
            }
        }
    }

    private static bool TryParseSearchPatterns(
        string filePattern,
        out IReadOnlyList<string> patterns)
    {
        var parsed = (filePattern ?? string.Empty)
            .Split(';', StringSplitOptions.TrimEntries
                        | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (parsed.Count == 0)
        {
            parsed.Add("*");
        }

        var valid = parsed.Count <= 16
                    && parsed.All(pattern => pattern.Length <= 128
                        && pattern.IndexOf('\0') < 0
                        && !pattern.Any(char.IsControl)
                        && !pattern.Contains("..", StringComparison.Ordinal));
        patterns = parsed;
        return valid;
    }

    private static bool MatchesSearchPattern(
        string path,
        string displayPath,
        IReadOnlyList<string> patterns)
    {
        var name = Path.GetFileName(path);
        var normalizedDisplay = NormalizeDisplayPath(displayPath);
        return patterns.Any(pattern =>
            FileSystemName.MatchesSimpleExpression(
                pattern,
                name,
                ignoreCase: true)
            || FileSystemName.MatchesSimpleExpression(
                NormalizeDisplayPath(pattern),
                normalizedDisplay,
                ignoreCase: true));
    }

    private static bool IsExcludedSearchFile(string path) =>
        SearchExcludedExtensions.Contains(Path.GetExtension(path));

    private static bool LooksBinary(string text)
    {
        if (text.IndexOf('\0') >= 0)
        {
            return true;
        }

        if (text.Length == 0)
        {
            return false;
        }

        var controls = text.Count(character =>
            char.IsControl(character)
            && character is not '\r' and not '\n' and not '\t');
        return controls > Math.Max(8, text.Length / 50);
    }

    private static string CreateSearchPreview(string line)
    {
        var preview = line.Trim().Replace('\t', ' ');
        return preview.Length <= 240 ? preview : preview[..237] + "...";
    }

    private static string NormalizeDisplayPath(string path) =>
        path.Replace('\\', '/');

    private static string GetRemoteDisplayPath(string root, string path)
    {
        var prefix = root.TrimEnd('/');
        return path.StartsWith(prefix + "/", StringComparison.Ordinal)
            ? path[(prefix.Length + 1)..]
            : path.TrimStart('/');
    }

    private sealed class SearchAccumulator(
        string root,
        string query,
        string filePattern)
    {
        private readonly List<WorkspaceAiTextSearchMatch> _matches = [];

        public int FilesScanned { get; private set; }
        public int FilesSkipped { get; private set; }
        public int EntriesVisited { get; private set; }
        public long BytesScanned { get; private set; }
        public bool IsTruncated { get; private set; }

        public bool MatchLimitReached =>
            _matches.Count >= MaximumSearchMatches;

        public bool ShouldStop =>
            MatchLimitReached
            || FilesScanned >= MaximumSearchFiles
            || EntriesVisited >= MaximumSearchEntries
            || BytesScanned >= MaximumSearchTotalBytes;

        public bool TryVisitEntry()
        {
            if (EntriesVisited >= MaximumSearchEntries)
            {
                IsTruncated = true;
                return false;
            }

            EntriesVisited++;
            return true;
        }

        public void Truncate() => IsTruncated = true;

        public bool CanRead(long length)
        {
            if (FilesScanned >= MaximumSearchFiles
                || length < 0
                || BytesScanned + length > MaximumSearchTotalBytes)
            {
                IsTruncated = true;
                return false;
            }

            return true;
        }

        public bool TryBeginFile(long length)
        {
            if (!CanRead(length))
            {
                return false;
            }

            FilesScanned++;
            BytesScanned += length;
            return true;
        }

        public void SkipFile() => FilesSkipped++;

        public void SkipDecodedFile()
        {
            FilesSkipped++;
        }

        public void AddMatch(
            string path,
            string displayPath,
            int line,
            int column,
            string preview)
        {
            if (MatchLimitReached)
            {
                IsTruncated = true;
                return;
            }

            _matches.Add(new WorkspaceAiTextSearchMatch(
                path,
                displayPath,
                line,
                column,
                preview));
            if (MatchLimitReached)
            {
                IsTruncated = true;
            }
        }

        public WorkspaceAiTextSearchResult ToResult()
        {
            if (ShouldStop)
            {
                IsTruncated = true;
            }

            return new WorkspaceAiTextSearchResult(
                root,
                query,
                filePattern,
                _matches,
                FilesScanned,
                FilesSkipped,
                BytesScanned,
                IsTruncated);
        }
    }

    private static HttpClient CreateNetworkClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip
                                     | DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(
                    context.DnsEndPoint.Host,
                    cancellationToken);
                var address = addresses.FirstOrDefault(IsPublicInternetAddress)
                              ?? throw new HttpRequestException(
                                  "The host does not resolve to a public internet address.");
                var socket = new Socket(
                    address.AddressFamily,
                    SocketType.Stream,
                    ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(
                        address,
                        context.DnsEndPoint.Port,
                        cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    private static bool IsPublicInternetAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6LinkLocal
            || address.IsIPv6SiteLocal
            || address.IsIPv6Multicast)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] switch
            {
                0 or 10 or 127 => false,
                100 when bytes[1] is >= 64 and <= 127 => false,
                169 when bytes[1] == 254 => false,
                172 when bytes[1] is >= 16 and <= 31 => false,
                192 when bytes[1] == 168 => false,
                >= 224 => false,
                _ => true
            };
        }

        // fc00::/7 is IPv6 unique-local space.
        return bytes.Length == 16 && (bytes[0] & 0xFE) != 0xFC;
    }

    private static bool IsTextMediaType(string mediaType) =>
        mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
        || mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase);

    private WorkspaceAiOperationResult<T> Rejected<T>(
        WorkspaceAiAuthorization authorization)
    {
        TryRecordDecision(authorization, authorization.RelativePath);
        return new WorkspaceAiOperationResult<T>
        {
            Status = authorization.Outcome
                     == WorkspaceAiAuthorizationOutcome.ConfirmationRequired
                ? WorkspaceAiOperationStatus.ConfirmationRequired
                : WorkspaceAiOperationStatus.Denied,
            Message = authorization.Message
        };
    }

    private bool TryRecordAuthorization(
        WorkspaceAiAuthorization authorization,
        out string failure,
        string? target = null)
    {
        try
        {
            _audit.Record(CreateAuditEntry(
                authorization,
                WorkspaceAiAuditOutcome.Authorized,
                "The host permission broker authorized this bounded attempt.",
                target));
            failure = string.Empty;
            return true;
        }
        catch (Exception exception) when (IsAuditException(exception))
        {
            failure =
                $"The local AI audit trail is unavailable, so the operation was not performed: {exception.Message}";
            return false;
        }
    }

    private void TryRecordDecision(
        WorkspaceAiAuthorization authorization,
        string? target)
    {
        try
        {
            _audit.Record(CreateAuditEntry(
                authorization,
                authorization.Outcome
                == WorkspaceAiAuthorizationOutcome.ConfirmationRequired
                    ? WorkspaceAiAuditOutcome.ConfirmationRequired
                    : WorkspaceAiAuditOutcome.Denied,
                authorization.Message,
                target));
        }
        catch (Exception exception) when (IsAuditException(exception))
        {
        }
    }

    private void RecordCompletion(
        WorkspaceAiAuthorization authorization,
        WorkspaceAiAuditOutcome outcome,
        string detail,
        string? target = null)
    {
        try
        {
            _audit.Record(CreateAuditEntry(
                authorization,
                outcome,
                detail,
                target));
        }
        catch (Exception exception) when (IsAuditException(exception))
        {
        }
    }

    private WorkspaceAiOperationResult<T> FailAfterAuthorization<T>(
        WorkspaceAiAuthorization authorization,
        string message)
    {
        RecordCompletion(
            authorization,
            WorkspaceAiAuditOutcome.Failed,
            message);
        return Failed<T>(message);
    }

    private WorkspaceAiAuditEntry CreateAuditEntry(
        WorkspaceAiAuthorization authorization,
        WorkspaceAiAuditOutcome outcome,
        string detail,
        string? target) =>
        new()
        {
            TimestampUtc = _timeProvider.GetUtcNow(),
            WorkspaceId = authorization.WorkspaceId,
            ConversationId = _auditConversationId.Value ?? string.Empty,
            RemoteHostId = WorkspaceLocation.TryParse(
                               authorization.WorkspaceUri,
                               out var location)
                           && location.IsSsh
                ? location.Authority
                : string.Empty,
            WorkspaceUri = authorization.WorkspaceUri,
            Operation = authorization.Operation,
            Capability = authorization.Capability,
            Target = string.IsNullOrWhiteSpace(target)
                ? string.IsNullOrWhiteSpace(authorization.RelativePath)
                    ? "."
                    : authorization.RelativePath
                : target,
            Outcome = outcome,
            Detail = detail
        };

    private sealed class AuditScope(Action restore) : IDisposable
    {
        private Action? _restore = restore;

        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }

    private static WorkspaceAiOperationResult<T> Success<T>(
        T value,
        string message) =>
        new()
        {
            Status = WorkspaceAiOperationStatus.Succeeded,
            Message = message,
            Value = value
        };

    private static WorkspaceAiOperationResult<T> Failed<T>(string message) =>
        new()
        {
            Status = WorkspaceAiOperationStatus.Failed,
            Message = message
        };

    private (IWorkspaceFileSystemProvider Provider, WorkspaceLocation Location)
        ResolveRemote(WorkspaceAiAuthorization authorization)
    {
        if (_workspaceProviders is null
            || !WorkspaceLocation.TryParse(
                authorization.WorkspaceUri,
                out var location)
            || !location.IsSsh)
        {
            throw new WorkspaceFileSystemException(
                WorkspaceFailureKind.Connection,
                "The remote workspace provider is not available in this Hypershell session.");
        }

        return (_workspaceProviders.Resolve(location), location);
    }

    private static bool IsFileOperationException(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException
            or NotSupportedException
            or WorkspaceFileSystemException;

    private static bool IsAuditException(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or JsonException;

    private static bool IsVisualPageOperationException(Exception exception) =>
        exception is VisualPageValidationException
            or VisualPageStaleRevisionException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or JsonException
            or ArgumentException;

    private static bool IsSha256(string? value) =>
        value is { Length: 64 }
        && value.All(Uri.IsHexDigit);

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static string HashSensitiveText(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            return Hash(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
