namespace HyperLib.Runtime;

internal static class LibraryPathGuard
{
    public static string Within(string root, string path)
    {
        var boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.Equals(boundary, comparison) && !full.StartsWith(boundary + Path.DirectorySeparatorChar, comparison))
            throw new IOException("Project path escapes the HyperLib root.");
        NoLinks(full);
        return full;
    }
    public static void NoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Symbolic links and reparse points are not allowed in template paths.");
        }
    }
    public static IEnumerable<string> Files(string root)
    {
        NoLinks(root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            NoLinks(entry);
            if (Directory.Exists(entry))
            {
                if (Path.GetFileName(entry) is "bin" or "obj" or ".git" or ".hyperlib" or "node_modules") continue;
                foreach (var nested in Files(entry)) yield return nested;
            }
            else yield return entry;
        }
    }

    public static async Task WriteNewAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        NoLinks(path);
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(contents, ct);
                await stream.FlushAsync(ct);
                stream.Flush(true);
            }
            ct.ThrowIfCancellationRequested();
            NoLinks(path);
            try { File.Move(temporary, path, overwrite: false); }
            // Only a competing completed destination is harmless. Write/flush failures propagate.
            catch (IOException) when (File.Exists(path)) { NoLinks(path); }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task ReplaceAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        NoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(contents, ct);
                await stream.FlushAsync(ct);
                stream.Flush(true);
            }
            ct.ThrowIfCancellationRequested();
            NoLinks(path);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
