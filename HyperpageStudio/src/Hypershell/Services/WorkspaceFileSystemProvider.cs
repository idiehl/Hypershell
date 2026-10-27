using Hypershell.Models;
using System.Collections.Concurrent;
using System.IO;

namespace Hypershell.Services;

public interface IWorkspaceFileSystemProvider : IAsyncDisposable
{
    string ProviderId { get; }

    string Authority { get; }

    StringComparer PathComparer { get; }

    WorkspaceLocation Normalize(WorkspaceLocation location);

    WorkspaceLocation Combine(
        WorkspaceLocation directory,
        string childName);

    WorkspaceLocation? GetParent(WorkspaceLocation location);

    Task<IReadOnlyList<WorkspaceEntry>> ListAsync(
        WorkspaceLocation directory,
        CancellationToken cancellationToken = default);

    Task<WorkspaceEntry?> StatAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default);

    Task<WorkspaceFileContent> ReadFileAsync(
        WorkspaceLocation location,
        long maximumBytes,
        CancellationToken cancellationToken = default);

    Task CreateDirectoryAsync(
        WorkspaceLocation location,
        CancellationToken cancellationToken = default);

    Task CreateFileAsync(
        WorkspaceLocation location,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);

    Task<string> ReplaceFileAsync(
        WorkspaceLocation location,
        ReadOnlyMemory<byte> content,
        WorkspaceWriteOptions options,
        CancellationToken cancellationToken = default);

    Task RenameAsync(
        WorkspaceLocation location,
        string newName,
        CancellationToken cancellationToken = default);

    Task MoveAsync(
        WorkspaceLocation source,
        WorkspaceLocation destination,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        WorkspaceLocation location,
        WorkspaceDeleteConfirmation confirmation,
        CancellationToken cancellationToken = default);

    Task DownloadAsync(
        WorkspaceLocation source,
        Stream destination,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);

    Task UploadAsync(
        Stream source,
        WorkspaceLocation destination,
        bool overwrite,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class WorkspaceFileSystemProviderRegistry : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, IWorkspaceFileSystemProvider>
        _providers = new(StringComparer.OrdinalIgnoreCase);

    public WorkspaceFileSystemProviderRegistry(
        IWorkspaceFileSystemProvider localProvider)
    {
        Register(localProvider);
    }

    public void Register(IWorkspaceFileSystemProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var key = CreateKey(provider.ProviderId, provider.Authority);
        if (_providers.TryGetValue(key, out var previous)
            && !ReferenceEquals(previous, provider))
        {
            _ = previous.DisposeAsync();
        }

        _providers[key] = provider;
    }

    public async Task RemoveAsync(string providerId, string authority)
    {
        if (_providers.TryRemove(
                CreateKey(providerId, authority),
                out var provider))
        {
            await provider.DisposeAsync();
        }
    }

    public IWorkspaceFileSystemProvider Resolve(WorkspaceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return _providers.TryGetValue(
            CreateKey(location.ProviderId, location.Authority),
            out var provider)
            ? provider
            : throw new WorkspaceFileSystemException(
                WorkspaceFailureKind.Connection,
                $"Workspace provider '{location.ProviderId}://{location.Authority}' is not available.");
    }

    public bool IsRegistered(string providerId, string authority) =>
        _providers.ContainsKey(CreateKey(providerId, authority));

    public async ValueTask DisposeAsync()
    {
        var providers = _providers.Values.Distinct().ToArray();
        _providers.Clear();
        foreach (var provider in providers)
        {
            await provider.DisposeAsync();
        }
    }

    private static string CreateKey(string providerId, string authority) =>
        (providerId?.Trim().ToLowerInvariant() ?? string.Empty)
        + "://"
        + (authority?.Trim().ToLowerInvariant() ?? string.Empty);
}
