using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hypershell.Services;

public sealed record WorkspaceAiExactEditResult(
    bool Succeeded,
    string Message,
    string Content,
    int EditCount);

/// <summary>
/// Applies bounded, exact SEARCH/REPLACE blocks supplied outside JSON. The
/// caller still owns path authorization and the current-file SHA precondition.
/// </summary>
public static partial class WorkspaceAiExactEdit
{
    public const int MaximumEditBlocks = 32;

    [GeneratedRegex(
        "^<<<<<<< SEARCH[ \\t]*\\n(?<search>.*?)^=======[ \\t]*\\n(?<replacement>.*?)^>>>>>>> REPLACE[ \\t]*(?:\\n|$)",
        RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex EditBlockRegex();

    public static WorkspaceAiExactEditResult Apply(
        string currentContent,
        string transferPayload)
    {
        ArgumentNullException.ThrowIfNull(currentContent);
        ArgumentNullException.ThrowIfNull(transferPayload);

        var normalizedCurrent = NormalizeLineEndings(currentContent);
        var normalizedPayload = NormalizeLineEndings(transferPayload);
        var matches = EditBlockRegex().Matches(normalizedPayload);
        if (matches.Count == 0)
        {
            return Failed(
                currentContent,
                "No exact-edit block was found. Return one or more <<<<<<< SEARCH / ======= / >>>>>>> REPLACE blocks and no commentary.");
        }

        if (matches.Count > MaximumEditBlocks)
        {
            return Failed(
                currentContent,
                $"The exact edit exceeds the {MaximumEditBlocks}-block limit.");
        }

        var previousEnd = 0;
        foreach (Match match in matches)
        {
            if (!string.IsNullOrWhiteSpace(
                    normalizedPayload[previousEnd..match.Index]))
            {
                return Failed(
                    currentContent,
                    "Exact-edit transfer contained text outside its SEARCH/REPLACE blocks.");
            }

            previousEnd = match.Index + match.Length;
        }

        if (!string.IsNullOrWhiteSpace(normalizedPayload[previousEnd..]))
        {
            return Failed(
                currentContent,
                "Exact-edit transfer contained trailing commentary outside its SEARCH/REPLACE blocks.");
        }

        var edited = normalizedCurrent;
        var applied = 0;
        foreach (Match match in matches)
        {
            var search = RemoveMarkerSeparatorNewline(
                match.Groups["search"].Value);
            var replacement = RemoveMarkerSeparatorNewline(
                match.Groups["replacement"].Value);
            if (search.Length == 0)
            {
                return Failed(
                    currentContent,
                    $"Exact-edit block {applied + 1} has an empty SEARCH section.");
            }

            var first = edited.IndexOf(search, StringComparison.Ordinal);
            if (first < 0)
            {
                return Failed(
                    currentContent,
                    $"Exact-edit block {applied + 1} did not match the current file. Read the current lines and copy the SEARCH text exactly.");
            }

            var second = edited.IndexOf(
                search,
                first + search.Length,
                StringComparison.Ordinal);
            if (second >= 0)
            {
                return Failed(
                    currentContent,
                    $"Exact-edit block {applied + 1} matched more than once. Include more unchanged context so the SEARCH text is unique.");
            }

            edited = string.Concat(
                edited.AsSpan(0, first),
                replacement,
                edited.AsSpan(first + search.Length));
            applied++;
        }

        if (string.Equals(edited, normalizedCurrent, StringComparison.Ordinal))
        {
            return Failed(
                currentContent,
                "The exact edit produced no change.");
        }

        var lineEnding = DetectLineEnding(currentContent);
        var restored = lineEnding == "\n"
            ? edited
            : edited.Replace("\n", lineEnding, StringComparison.Ordinal);
        return new WorkspaceAiExactEditResult(
            true,
            $"Applied {applied} exact edit block(s).",
            restored,
            applied);
    }

    private static WorkspaceAiExactEditResult Failed(
        string currentContent,
        string message) =>
        new(false, message, currentContent, 0);

    private static string RemoveMarkerSeparatorNewline(string value) =>
        value.EndsWith('\n') ? value[..^1] : value;

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static string DetectLineEnding(string value)
    {
        var crLf = value.IndexOf("\r\n", StringComparison.Ordinal);
        if (crLf >= 0)
        {
            return "\r\n";
        }

        return value.IndexOf('\r') >= 0 ? "\r" : "\n";
    }
}

public sealed record WorkspaceAiProjectCheckPlan(
    string ProjectKind,
    string RequestedCheck,
    string EffectiveCheck,
    string Target,
    string Command,
    string Description);

public sealed record WorkspaceAiProjectCheckPlanningResult(
    bool Succeeded,
    string Message,
    WorkspaceAiProjectCheckPlan? Plan = null);

/// <summary>
/// Maps recognized project manifests to conventional, bounded verification
/// commands. The selected Context and terminal policy still authorize and run
/// the resulting command.
/// </summary>
public static class WorkspaceAiProjectCheckPlanner
{
    private static readonly HashSet<string> Checks =
        new(StringComparer.Ordinal)
        {
            "build",
            "test",
            "lint",
            "typecheck"
        };

    public static WorkspaceAiProjectCheckPlanningResult Create(
        string requestedCheck,
        IReadOnlyList<WorkspaceAiDirectoryEntry> rootEntries,
        string? packageJson,
        bool posixShell)
    {
        ArgumentNullException.ThrowIfNull(rootEntries);
        var check = (requestedCheck ?? string.Empty).Trim().ToLowerInvariant();
        if (!Checks.Contains(check))
        {
            return Failed(
                "Project check must be build, test, lint, or typecheck.");
        }

        var files = rootEntries
            .Where(entry => !entry.IsDirectory)
            .Select(entry => entry.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directories = rootEntries
            .Where(entry => entry.IsDirectory)
            .Select(entry => entry.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dotnetTarget = rootEntries
            .Where(entry => !entry.IsDirectory)
            .Select(entry => entry.Name)
            .OrderBy(name => DotNetTargetPriority(name))
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(name => DotNetTargetPriority(name) < int.MaxValue);
        if (dotnetTarget is not null)
        {
            var effective = check is "lint" or "typecheck" ? "build" : check;
            var verb = effective == "test" ? "test" : "build";
            return Success(new WorkspaceAiProjectCheckPlan(
                "dotnet",
                check,
                effective,
                dotnetTarget,
                $"dotnet {verb} {Quote(dotnetTarget, posixShell)} --nologo --verbosity minimal",
                effective == check
                    ? $"Run the .NET {effective} check."
                    : $"Use a .NET build as the conventional {check} check."));
        }

        if (files.Contains("package.json"))
        {
            var script = ResolvePackageScript(check, packageJson);
            if (script is null)
            {
                return Failed(
                    $"package.json does not define a conventional {check} script.");
            }

            var runner = files.Contains("pnpm-lock.yaml")
                ? "pnpm run"
                : files.Contains("yarn.lock")
                    ? "yarn run"
                    : files.Contains("bun.lock") || files.Contains("bun.lockb")
                        ? "bun run"
                        : "npm run";
            return Success(new WorkspaceAiProjectCheckPlan(
                "node",
                check,
                check,
                "package.json",
                $"{runner} {Quote(script, posixShell)}",
                $"Run the package.json '{script}' script."));
        }

        if (files.Contains("Cargo.toml"))
        {
            var command = check switch
            {
                "test" => "cargo test",
                "lint" => "cargo clippy --all-targets --all-features",
                _ => "cargo check --all-targets"
            };
            return Success(new WorkspaceAiProjectCheckPlan(
                "rust",
                check,
                check is "build" or "typecheck" ? "check" : check,
                "Cargo.toml",
                command,
                "Run the conventional Cargo project check."));
        }

        if (files.Contains("go.mod"))
        {
            var command = check switch
            {
                "test" => "go test ./...",
                "lint" or "typecheck" => "go vet ./...",
                _ => "go build ./..."
            };
            return Success(new WorkspaceAiProjectCheckPlan(
                "go",
                check,
                check,
                "go.mod",
                command,
                "Run the conventional Go project check."));
        }

        if (files.Contains("pyproject.toml")
            || files.Contains("requirements.txt")
            || files.Contains("setup.py")
            || files.Contains("pytest.ini"))
        {
            var command = check switch
            {
                "test" => "python -m pytest",
                "lint" => "python -m ruff check .",
                _ => "python -m compileall -q ."
            };
            return Success(new WorkspaceAiProjectCheckPlan(
                "python",
                check,
                check is "build" or "typecheck" ? "syntax" : check,
                files.First(name => name.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)
                                    || name.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)
                                    || name.Equals("setup.py", StringComparison.OrdinalIgnoreCase)
                                    || name.Equals("pytest.ini", StringComparison.OrdinalIgnoreCase)),
                command,
                "Run the conventional Python project check."));
        }

        if (files.Contains("pom.xml"))
        {
            var executable = files.Contains("mvnw.cmd") && !posixShell
                ? ".\\mvnw.cmd"
                : files.Contains("mvnw") && posixShell
                    ? "./mvnw"
                    : "mvn";
            var command = check switch
            {
                "test" => $"{executable} -q test",
                "lint" or "typecheck" => $"{executable} -q -DskipTests compile",
                _ => $"{executable} -q -DskipTests package"
            };
            return Success(new WorkspaceAiProjectCheckPlan(
                "maven",
                check,
                check,
                "pom.xml",
                command,
                "Run the conventional Maven project check."));
        }

        var gradleManifest = files.FirstOrDefault(name =>
            name.Equals("build.gradle", StringComparison.OrdinalIgnoreCase)
            || name.Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase));
        if (gradleManifest is not null)
        {
            var executable = files.Contains("gradlew.bat") && !posixShell
                ? ".\\gradlew.bat"
                : files.Contains("gradlew") && posixShell
                    ? "./gradlew"
                    : "gradle";
            var task = check switch
            {
                "test" => "test",
                "lint" => "check",
                "typecheck" => "classes",
                _ => "build"
            };
            return Success(new WorkspaceAiProjectCheckPlan(
                "gradle",
                check,
                task,
                gradleManifest,
                $"{executable} {task}",
                "Run the conventional Gradle project check."));
        }

        if (files.Contains("CMakeLists.txt") && directories.Contains("build"))
        {
            if (check is "test")
            {
                return Success(new WorkspaceAiProjectCheckPlan(
                    "cmake",
                    check,
                    check,
                    "CMakeLists.txt",
                    "ctest --test-dir build --output-on-failure",
                    "Run tests from the configured CMake build tree."));
            }

            if (check is "build")
            {
                return Success(new WorkspaceAiProjectCheckPlan(
                    "cmake",
                    check,
                    check,
                    "CMakeLists.txt",
                    "cmake --build build",
                    "Build the configured CMake project."));
            }
        }

        return Failed(
            "No supported project manifest or conventional check was found at the conversation working-directory root. Use an explicit terminal command only after inspecting the project documentation.");
    }

    private static int DotNetTargetPriority(string name) =>
        Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".slnx" => 0,
            ".sln" => 1,
            ".csproj" => 2,
            ".fsproj" => 3,
            ".vbproj" => 4,
            _ => int.MaxValue
        };

    private static string? ResolvePackageScript(
        string check,
        string? packageJson)
    {
        if (string.IsNullOrWhiteSpace(packageJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(packageJson);
            if (!document.RootElement.TryGetProperty("scripts", out var scripts)
                || scripts.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var candidates = check switch
            {
                "typecheck" => new[] { "typecheck", "type-check", "check" },
                _ => new[] { check }
            };
            return candidates.FirstOrDefault(candidate =>
                scripts.TryGetProperty(candidate, out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Quote(string value, bool posixShell) =>
        posixShell
            ? "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'"
            : "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static WorkspaceAiProjectCheckPlanningResult Success(
        WorkspaceAiProjectCheckPlan plan) =>
        new(true, plan.Description, plan);

    private static WorkspaceAiProjectCheckPlanningResult Failed(
        string message) =>
        new(false, message);
}
