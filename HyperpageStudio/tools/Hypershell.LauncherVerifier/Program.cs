using Hypershell.Launcher;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json;

if (args is ["--capture", var destination, ..])
{
    await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(new
    {
        arguments = args.Skip(2).ToArray(),
        directory = Environment.CurrentDirectory,
        appId = Environment.GetEnvironmentVariable("HYPERSHELL_APP_USER_MODEL_ID"),
        relaunch = Environment.GetEnvironmentVariable("HYPERSHELL_RELAUNCH_COMMAND"),
        icon = Environment.GetEnvironmentVariable("HYPERSHELL_RELAUNCH_ICON_RESOURCE"),
        displayName = Environment.GetEnvironmentVariable("HYPERSHELL_RELAUNCH_DISPLAY_NAME"),
        hyperlib = Environment.GetEnvironmentVariable("HYPERLIB_ROOT"),
        profile = Environment.GetEnvironmentVariable("HYPERSHELL_APP_DATA")
    }));
    return;
}

var fixture = Path.Combine(Path.GetTempPath(), "Hypershell-LauncherVerifier-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var launcherDir = Directory.CreateDirectory(Path.Combine(fixture, "launcher")).FullName;
var appDir = Directory.CreateDirectory(Path.Combine(fixture, "application")).FullName;
var workingDir = Directory.CreateDirectory(Path.Combine(fixture, "work with spaces")).FullName;
var hyperlib = Directory.CreateDirectory(Path.Combine(fixture, "library")).FullName;
var profile = Path.Combine(fixture, "new profile");
var launcher = Path.Combine(launcherDir, "Hypershell.exe");
var executable = Path.Combine(appDir, "Hypershell.exe");
var manifest = Path.Combine(launcherDir, "launch-target.json");
File.WriteAllText(launcher, "fixture launcher");
File.WriteAllText(executable, "fixture target");
var assertions = 0;

try
{
    Dictionary<string, object?> Valid() => new()
    {
        ["schemaVersion"] = 1,
        ["executablePath"] = executable,
        ["workingDirectory"] = workingDir,
        ["hyperlibRoot"] = hyperlib,
        ["profileDirectory"] = profile
    };
    void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    LaunchTarget Load(Dictionary<string, object?>? config = null)
    {
        File.WriteAllText(manifest, JsonSerializer.Serialize(config ?? Valid()));
        return LaunchTarget.Load(manifest, launcher);
    }
    void Reject(string label, string json)
    {
        File.WriteAllText(manifest, json);
        try { _ = LaunchTarget.Load(manifest, launcher); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or JsonException)
        { assertions++; return; }
        throw new InvalidOperationException("Accepted invalid target: " + label);
    }
    void RejectValue(string key, object? value)
    {
        var config = Valid();
        config[key] = value;
        Reject(key + "=" + value, JsonSerializer.Serialize(config));
    }

    var target = Load();
    var trickyArguments = new[] { "a path with spaces", "quoted \"value\"", "", "Unicode ✨", @"trailing\", @"C:\a b\" };
    var start = target.CreateStartInfo(trickyArguments);
    Check(start.FileName == executable && start.WorkingDirectory == workingDir, "Explicit target/working directory was not preserved.");
    Check(!start.UseShellExecute && start.CreateNoWindow, "Launcher must not start a console window.");
    Check(start.ArgumentList.SequenceEqual(trickyArguments), "Arguments were changed or shell-quoted.");
    Check(start.Environment["HYPERSHELL_APP_USER_MODEL_ID"] == "Hypershell.Desktop", "AUMID is unstable.");
    Check(start.Environment["HYPERSHELL_RELAUNCH_COMMAND"] == $"\"{launcher}\"", "Relaunch command must quote launcher path.");
    Check(start.Environment["HYPERSHELL_RELAUNCH_ICON_RESOURCE"] == executable + ",-32512", "Relaunch icon must use the selected app publish, not the cached stable launcher.");
    Check(start.Environment["HYPERSHELL_RELAUNCH_DISPLAY_NAME"] == "Hypershell", "Display name missing.");
    Check(start.Environment[LaunchTarget.RecursionEnvironmentVariable] == "1", "Recursive launch guard missing.");
    Check(start.Environment["HYPERLIB_ROOT"] == hyperlib && start.Environment["HYPERSHELL_APP_DATA"] == profile, "Optional environments were not applied.");
    Check(!Directory.Exists(profile), "Launcher created or changed profile contents.");

    var optional = Valid();
    optional.Remove("hyperlibRoot"); optional["profileDirectory"] = null;
    var inherit = Load(optional).CreateStartInfo([]);
    inherit.Environment.TryGetValue("HYPERLIB_ROOT", out var inheritedLibrary);
    inherit.Environment.TryGetValue("HYPERSHELL_APP_DATA", out var inheritedProfile);
    Check(inheritedLibrary == Environment.GetEnvironmentVariable("HYPERLIB_ROOT"), "Omitted Hyperlib changed caller environment.");
    Check(inheritedProfile == Environment.GetEnvironmentVariable("HYPERSHELL_APP_DATA"), "Null profile changed caller environment.");

    Reject("syntax", "{bad json}");
    Reject("array", "[]");
    Reject("size", new string(' ', 16385));
    Reject("duplicate", "{\"schemaVersion\":1,\"schemaVersion\":1}");
    RejectValue("unexpected", "value");
    RejectValue("schemaVersion", 2);
    RejectValue("schemaVersion", "1");
    RejectValue("executablePath", null);
    RejectValue("executablePath", "Hypershell.exe");
    RejectValue("executablePath", "C:Hypershell.exe");
    RejectValue("executablePath", "http://127.0.0.1/Hypershell.exe");
    RejectValue("executablePath", @"\\server\app\Hypershell.exe");
    RejectValue("executablePath", @"\\?\C:\app\Hypershell.exe");
    RejectValue("executablePath", executable + ":stream");
    RejectValue("executablePath", @"C:\%TEMP%\Hypershell.exe");
    RejectValue("executablePath", Path.ChangeExtension(executable, ".cmd"));
    RejectValue("executablePath", Path.Combine(appDir, "absent.exe"));
    RejectValue("workingDirectory", Path.Combine(fixture, "absent"));
    RejectValue("hyperlibRoot", Path.Combine(fixture, "absent"));
    RejectValue("profileDirectory", executable);
    RejectValue("executablePath", launcher);
    RejectValue("executablePath", launcher.ToUpperInvariant());
    RejectValue("executablePath", Path.Combine(launcherDir, "..", "launcher", "Hypershell.exe"));

    var hardLink = Path.Combine(appDir, "launcher-alias.exe");
    if (!Native.CreateHardLink(hardLink, launcher, IntPtr.Zero))
        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not create identity-check fixture.");
    RejectValue("executablePath", hardLink);
    File.WriteAllText(Path.Combine(appDir, "launch-target.json"), "{}");
    RejectValue("executablePath", executable);
    File.Delete(Path.Combine(appDir, "launch-target.json"));

    var absentManifest = Path.Combine(launcherDir, "missing.json");
    try { _ = LaunchTarget.Load(absentManifest, launcher); throw new InvalidOperationException("Missing manifest accepted."); }
    catch (FileNotFoundException) { assertions++; }

    var publishedLauncher = Path.Combine(AppContext.BaseDirectory, "Hypershell.exe");
    using (var file = File.OpenRead(publishedLauncher))
    using (var pe = new PEReader(file))
        Check(pe.PEHeaders.PEHeader?.Subsystem == Subsystem.WindowsGui, "Launcher is not a Windows GUI executable.");
    Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "Hypershell.ico")), "Shared icon missing from launcher output.");

    // The only process started by this suite is this verifier's capture mode.
    var self = Path.Combine(AppContext.BaseDirectory, "Hypershell.LauncherVerifier.exe");
    var capturePath = Path.Combine(fixture, "captured.json");
    var captureConfig = Valid(); captureConfig["executablePath"] = self;
    var captureTarget = Load(captureConfig);
    using var child = Process.Start(captureTarget.CreateStartInfo(["--capture", capturePath, .. trickyArguments]))
        ?? throw new InvalidOperationException("Capture fixture did not start.");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await child.WaitForExitAsync(timeout.Token);
    Check(child.ExitCode == 0, "Capture process failed.");
    using var captured = JsonDocument.Parse(await File.ReadAllTextAsync(capturePath));
    var capturedRoot = captured.RootElement;
    Check(capturedRoot.GetProperty("arguments").EnumerateArray().Select(a => a.GetString()).SequenceEqual(trickyArguments), "Windows process argument forwarding changed values.");
    Check(capturedRoot.GetProperty("directory").GetString() == workingDir, "Actual child working directory differed.");
    Check(capturedRoot.GetProperty("appId").GetString() == LaunchTarget.ApplicationUserModelId, "Actual child AUMID environment differed.");
    Check(capturedRoot.GetProperty("relaunch").GetString() == $"\"{launcher}\"", "Actual child relaunch command differed.");
    Check(capturedRoot.GetProperty("icon").GetString() == self + ",-32512", "Actual child relaunch icon differed.");
    Check(capturedRoot.GetProperty("displayName").GetString() == "Hypershell", "Actual child display name differed.");
    Check(capturedRoot.GetProperty("hyperlib").GetString() == hyperlib, "Actual child library path differed.");
    Check(capturedRoot.GetProperty("profile").GetString() == profile, "Actual child profile path differed.");
    Console.WriteLine($"HYPERSHELL_LAUNCHER_OK assertions={assertions} actualArgumentForwarding=true");
}
finally
{
    // Only our unique, directly constructed fixture directory is removed.
    Directory.Delete(fixture, recursive: true);
}

internal static class Native
{
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateHardLink(string newName, string existingName, IntPtr attributes);
}
