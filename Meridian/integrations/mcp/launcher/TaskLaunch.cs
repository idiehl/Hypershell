using System.Diagnostics;
using System.Security.Cryptography;

namespace Meridian.TaskLauncher;

internal static class TaskLaunch
{
    internal static ProcessStartInfo Create(string mode, IReadOnlyDictionary<string, string> metadata)
    {
        if (mode is not ("backend" or "secure-tunnel")) throw new ArgumentException("Unknown fixed task mode.");
        var repo = RegularPath(metadata["MeridianRoot"]);
        var scripts = RegularPath(Path.Combine(repo, "integrations", "mcp", "scripts"));
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = mode == "backend" ? "Stable" : "SecureTunnel";
        var expectedShell = RegularPath(Path.Combine(local, "AtlasMeridian", "McpCompanion", profile, "bin", "PowerShell7", "pwsh.exe"));
        var pinnedShell = RegularPath(metadata[mode == "backend" ? "BackendPowerShell" : "SecurePowerShell"]);
        if (!StringComparer.OrdinalIgnoreCase.Equals(expectedShell, pinnedShell)) throw new InvalidOperationException("PowerShell location changed.");
        var script = RegularPath(Path.Combine(scripts, mode == "backend" ? "run-backend-task.ps1" : "run-secure-tunnel-task.ps1"));
        if (!File.Exists(script) || !File.Exists(pinnedShell)) throw new FileNotFoundException("A trusted task component is missing.");
        var expectedHash = metadata["PowerShellSha256"];
        using (var file = File.OpenRead(pinnedShell))
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(SHA256.HashData(file)), expectedHash))
                throw new InvalidOperationException("The pinned PowerShell binary changed.");
        }
        var info = new ProcessStartInfo(pinnedShell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = scripts,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-File", script }) info.ArgumentList.Add(arg);
        foreach (var name in info.Environment.Keys.ToArray())
        {
            if (name.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("COMPlus_", StringComparison.OrdinalIgnoreCase)
                || name is "NODE_OPTIONS" or "NODE_PATH" or "PSExecutionPolicyPreference") info.Environment.Remove(name);
        }
        return info;
    }

    internal static int StartAndWait(ProcessStartInfo info)
    {
        using var child = Process.Start(info) ?? throw new InvalidOperationException("The child process did not start.");
        child.WaitForExit();
        return child.ExitCode;
    }

    internal static string RegularPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0) throw new ArgumentException("A canonical trusted path is required.");
        var full = Path.GetFullPath(path);
        for (string? current = full; current != null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Task components cannot traverse reparse points.");
        }
        return full;
    }
}
