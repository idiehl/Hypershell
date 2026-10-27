using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hypershell.ProfileServer.Pages;

public sealed class StorageModel : PageModel
{
    private readonly StorageCapacityService _capacity;
    private readonly CloudMetadataStore _metadata;
    private readonly ProfileServerOptions _options;

    public StorageModel(
        StorageCapacityService capacity,
        CloudMetadataStore metadata,
        ProfileServerOptions options)
    {
        _capacity = capacity;
        _metadata = metadata;
        _options = options.Normalize();
    }

    public StorageCapacitySnapshot Capacity { get; private set; } = null!;
    public CloudMetadataStatus Metadata { get; private set; } = null!;
    public IReadOnlyList<StorageArea> Areas { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Capacity = _capacity.Inspect();
        Metadata = await _metadata.InspectAsync(cancellationToken);
        Areas = new[]
        {
            "accounts.json",
            "cloud-metadata.sqlite3",
            "profiles",
            "blobs",
            "data-protection-keys"
        }.Select(name => InspectArea(name)).ToList();
    }

    public string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024d / 1024d / 1024d:0.0} GiB",
        >= 1024L * 1024 => $"{bytes / 1024d / 1024d:0.0} MiB",
        >= 1024L => $"{bytes / 1024d:0.0} KiB",
        _ => $"{bytes} bytes"
    };

    private StorageArea InspectArea(string name)
    {
        var path = Path.Combine(_options.DataRoot, name);
        if (System.IO.File.Exists(path))
        {
            return new StorageArea(name, new FileInfo(path).Length, 1, false);
        }

        if (!Directory.Exists(path))
        {
            return new StorageArea(name, 0, 0, false);
        }

        const int maximumFiles = 100_000;
        long bytes = 0;
        var count = 0;
        var truncated = false;
        var enumeration = Directory.EnumerateFiles(
            path,
            "*",
            new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            });
        foreach (var file in enumeration)
        {
            if (count >= maximumFiles)
            {
                truncated = true;
                break;
            }

            try
            {
                bytes = checked(bytes + new FileInfo(file).Length);
                count++;
            }
            catch (Exception exception) when (exception is
                                              IOException
                                              or UnauthorizedAccessException
                                              or OverflowException)
            {
                truncated = true;
            }
        }

        return new StorageArea(name, bytes, count, truncated);
    }

    public sealed record StorageArea(
        string Name,
        long Bytes,
        int Files,
        bool IsTruncated);
}
