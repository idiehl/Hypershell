namespace Hypershell.ProfileServer;

public sealed record StorageCapacitySnapshot(
    long TotalBytes,
    long AvailableBytes,
    long ReservedBytes,
    bool CanAcceptWrites,
    DateTimeOffset CheckedAtUtc);

public sealed class StorageCapacityService
{
    private readonly string _dataRoot;
    private readonly long _reservedBytes;
    private readonly TimeProvider _timeProvider;

    public StorageCapacityService(
        ProfileServerOptions options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalized = options.Normalize();
        _dataRoot = normalized.DataRoot;
        _reservedBytes = normalized.MinimumFreeStorageBytes;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public StorageCapacitySnapshot Inspect(long pendingBytes = 0)
    {
        if (pendingBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pendingBytes));
        }

        Directory.CreateDirectory(_dataRoot);
        var root = Path.GetPathRoot(_dataRoot)
                   ?? throw new InvalidOperationException(
                       "The server data root has no storage volume.");
        var drive = new DriveInfo(root);
        var available = drive.AvailableFreeSpace;
        var canAccept = available >= checked(_reservedBytes + pendingBytes);
        return new StorageCapacitySnapshot(
            drive.TotalSize,
            available,
            _reservedBytes,
            canAccept,
            _timeProvider.GetUtcNow());
    }

    public bool CanAccept(long pendingBytes) =>
        Inspect(pendingBytes).CanAcceptWrites;
}
