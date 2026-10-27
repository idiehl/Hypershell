using Hypershell.Services;

var fixture = Path.Combine(Path.GetTempPath(), "Hypershell-SuiteVerifier", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var install = Path.Combine(fixture, "installed");
var profile = Path.Combine(fixture, "profile");
Directory.CreateDirectory(install);
Require(InstalledSuiteComponents.Discover(install, profile).Count == 0, "Core must not create optional app registrations.");
Require(!Directory.Exists(profile), "Core discovery must not create a library profile.");

Write("installed/Components/Authoring/studio/Hypershell.exe", "fixture");
Write("installed/Components/Authoring/Hyperlib/app/HyperLib.Host.exe", "fixture");
Write("installed/Components/Authoring/Hyperlib/hyperlib.app.json", "{}");
Write("installed/Components/Authoring/Hyperlib/BaseTemplates/example/default.json", "initial seed");
Write("installed/Components/Meridian/Meridian.Host.exe", "fixture");
var apps = InstalledSuiteComponents.Discover(install, profile);
Require(apps.Count == 3, "Complete must discover Studio, Hyperlib, and Meridian.");
Require(apps.All(app => app.Launch!.Executable.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)), "Registered executable paths must resolve inside the installed payload.");
var library = Path.Combine(profile, "Libraries", "Hyperlib");
Require(File.ReadAllText(Path.Combine(library, "BaseTemplates", "example", "default.json")) == "initial seed", "First launch must create the user library from the staged seed.");
Require(!Directory.Exists(Path.Combine(library, "app")), "Executable payloads must not be copied into the editable library.");
Require(new[] { "Media", "Content", "Works" }.All(name => Directory.Exists(Path.Combine(library, name))), "The user library needs its writable content folders.");
var studio = apps.Single(app => app.Id == "atlas.mynet.hyperpage-studio");
Require(studio.IsNative && studio.Launch!.Arguments.SequenceEqual(new[] { "--hyperpage-studio" }), "Studio must launch the installed native authoring application.");
Require(studio.Launch!.Environment["HYPERSHELL_STUDIO_BASE_DATA"] == profile, "Studio must use the requesting profile.");
var hyperlib = apps.Single(app => app.Id == "atlas.mynet.hyperlib");
Require(hyperlib.Launch!.WorkingDirectory == library && hyperlib.Launch.Arguments.Contains(library), "Hyperlib must use writable per-profile content.");
var meridian = apps.Single(app => app.Id == "atlas.meridian");
Require(meridian.Launch!.Arguments.Contains(Path.Combine(profile, "AtlasMeridian")), "Meridian must use the requesting profile's data directory.");

File.WriteAllText(Path.Combine(library, "BaseTemplates", "example", "default.json"), "user edit");
Write("installed/Components/Authoring/Hyperlib/BaseTemplates/example/default.json", "upgraded seed");
Write("installed/Components/Authoring/Hyperlib/BaseTemplates/example/new.json", "new seed file");
InstalledSuiteComponents.Discover(install, profile);
Require(File.ReadAllText(Path.Combine(library, "BaseTemplates", "example", "default.json")) == "user edit", "Upgrades must preserve existing user library content.");
Require(!File.Exists(Path.Combine(library, "BaseTemplates", "example", "new.json")), "Discovery must not overlay an existing library with updated seed files.");
Require(!Directory.EnumerateDirectories(Path.Combine(profile, "Libraries"), ".hyperlib-seed-*").Any(), "Seed creation must leave no temporary directories.");

var relocated = Path.Combine(fixture, "relocated");
Directory.Move(install, relocated);
apps = InstalledSuiteComponents.Discover(relocated, profile);
Require(apps.Count == 3 && apps.All(app => app.Launch!.Executable.StartsWith(relocated + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)), "Component discovery must not retain development or previous install paths.");

// Windows developer mode permits these unprivileged symlinks. Fail the verifier
// if links cannot be tested rather than silently reporting untested protection.
var hostileProfile = Path.Combine(fixture, "linked-profile");
Directory.CreateSymbolicLink(hostileProfile, profile);
try { ExpectInvalid(() => InstalledSuiteComponents.Discover(relocated, hostileProfile), "Linked destination profile must be rejected."); }
finally { Directory.Delete(hostileProfile); }
var seedLink = Path.Combine(relocated, "Components", "Authoring", "Hyperlib", "linked-content");
Directory.CreateSymbolicLink(seedLink, library);
var newProfile = Path.Combine(fixture, "new-profile");
try { ExpectInvalid(() => InstalledSuiteComponents.Discover(relocated, newProfile), "Linked seed content must be rejected."); }
finally { Directory.Delete(seedLink); }
Require(!Directory.Exists(Path.Combine(newProfile, "Libraries", "Hyperlib")), "Rejected seed content must not publish a partial library.");

Console.WriteLine("HYPERSHELL_SUITE_DISCOVERY_OK|core|three-apps|relocation|profile-isolation|seed-once|upgrade-preserves-content|links-rejected|atomic-seed");
// Test fixtures are deliberately retained for inspection; nothing invokes an
// installer, application, registration writer, or live user profile.
Console.WriteLine("FIXTURE|" + fixture);

void Write(string relative, string contents)
{
    var path = Path.Combine(fixture, relative.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, contents);
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static void ExpectInvalid(Action action, string message)
{
    try { action(); }
    catch (InvalidDataException) { return; }
    throw new InvalidOperationException(message);
}
