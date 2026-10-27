using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Hypershell.Launcher;

internal sealed record LaunchTarget(string ExecutablePath, string WorkingDirectory, string? HyperlibRoot, string? ProfileDirectory, string LauncherPath)
{
    public const string ApplicationUserModelId = "Hypershell.Desktop";
    public const string RecursionEnvironmentVariable = "HYPERSHELL_LAUNCHER_ACTIVE";
    private static readonly HashSet<string> ManifestProperties = new(StringComparer.Ordinal)
    { "schemaVersion", "executablePath", "workingDirectory", "hyperlibRoot", "profileDirectory" };

    public static LaunchTarget Load(string manifestPath, string launcherPath)
    {
        var info = new FileInfo(manifestPath);
        if (!info.Exists) throw new FileNotFoundException("The launch-target.json file is missing.", manifestPath);
        if (info.Length is < 2 or > 16384) throw new InvalidDataException("launch-target.json must contain a small JSON object (at most 16 KiB).");
        using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 4 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("launch-target.json must contain one JSON object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
            if (!ManifestProperties.Contains(property.Name) || !seen.Add(property.Name))
                throw new InvalidDataException($"The launcher setting '{property.Name}' is unknown or duplicated.");
        if (!root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out var number) || number != 1)
            throw new InvalidDataException("launch-target.json requires schemaVersion 1.");
        var executable = LocalAbsolutePath(ReadString(root, "executablePath", required: true)!, "executablePath");
        var workingDirectory = LocalAbsolutePath(ReadString(root, "workingDirectory", required: true)!, "workingDirectory");
        var hyperlib = OptionalDirectory(ReadString(root, "hyperlibRoot", required: false), "hyperlibRoot", mustExist: true);
        var profile = OptionalDirectory(ReadString(root, "profileDirectory", required: false), "profileDirectory", mustExist: false);
        launcherPath = Path.GetFullPath(launcherPath);

        if (!string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("executablePath must name the published application's .exe file.");
        if (!File.Exists(executable)) throw new FileNotFoundException("The configured Hypershell application was not found.", executable);
        if (!Directory.Exists(workingDirectory)) throw new DirectoryNotFoundException($"The configured workingDirectory does not exist: {workingDirectory}");
        if (string.Equals(executable, launcherPath, StringComparison.OrdinalIgnoreCase) || SamePhysicalFile(executable, launcherPath))
            throw new InvalidDataException("executablePath points back to this launcher. Select the published Hypershell application instead.");
        if (File.Exists(Path.Combine(Path.GetDirectoryName(executable)!, "launch-target.json")))
            throw new InvalidDataException("executablePath points into another launcher installation. Select the published Hypershell application instead.");
        return new(executable, workingDirectory, hyperlib, profile, launcherPath);
    }

    public ProcessStartInfo CreateStartInfo(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            WorkingDirectory = WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Normal
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["HYPERSHELL_APP_USER_MODEL_ID"] = ApplicationUserModelId;
        start.Environment["HYPERSHELL_RELAUNCH_COMMAND"] = $"\"{LauncherPath}\"";
        // Keep launch identity stable, but resolve the icon from the immutable
        // app publish so Explorer cannot reuse branding cached for the launcher.
        // ApplicationIcon is embedded as RT_GROUP_ICON resource 32512.
        start.Environment["HYPERSHELL_RELAUNCH_ICON_RESOURCE"] = $"{ExecutablePath},-32512";
        start.Environment["HYPERSHELL_RELAUNCH_DISPLAY_NAME"] = "Hypershell";
        start.Environment[RecursionEnvironmentVariable] = "1";
        if (HyperlibRoot is not null) start.Environment["HYPERLIB_ROOT"] = HyperlibRoot;
        if (ProfileDirectory is not null) start.Environment["HYPERSHELL_APP_DATA"] = ProfileDirectory;
        return start;
    }

    private static string? ReadString(JsonElement root, string name, bool required)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (required) throw new InvalidDataException($"launch-target.json requires {name}.");
            return null;
        }
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"{name} must be a non-empty path string.");
        return value.GetString();
    }

    private static string LocalAbsolutePath(string value, string setting)
    {
        // Device paths, UNC paths, URLs, drive-relative paths and expansion syntax
        // make a stable per-user target ambiguous. Installers write the exact path.
        if (value.Length < 3 || value.Length > 32760 || !char.IsAsciiLetter(value[0]) || value[1] != ':'
            || value[2] is not ('\\' or '/') || value.Any(char.IsControl) || value.IndexOf(':', 2) >= 0
            || value.Contains('"') || value.Contains('%') || value.Contains('*') || value.Contains('?'))
            throw new InvalidDataException($"{setting} must be an explicit absolute local path, such as C:\\Apps\\Hypershell\\Hypershell.exe.");
        var full = Path.GetFullPath(value);
        if (!Path.IsPathFullyQualified(full)) throw new InvalidDataException($"{setting} is not an absolute path.");
        return full;
    }

    private static string? OptionalDirectory(string? value, string setting, bool mustExist)
    {
        if (value is null) return null;
        var path = LocalAbsolutePath(value, setting);
        if (File.Exists(path)) throw new InvalidDataException($"{setting} points to a file instead of a directory.");
        if (mustExist && !Directory.Exists(path)) throw new DirectoryNotFoundException($"The configured {setting} does not exist: {path}");
        return path;
    }

    private static bool SamePhysicalFile(string left, string right)
    {
        using var leftHandle = File.OpenHandle(left, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var rightHandle = File.OpenHandle(right, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(leftHandle, out var a) || !GetFileInformationByHandle(rightHandle, out var b))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not verify the launch target's identity.");
        return a.VolumeSerialNumber == b.VolumeSerialNumber && a.FileIndexHigh == b.FileIndexHigh && a.FileIndexLow == b.FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
}
