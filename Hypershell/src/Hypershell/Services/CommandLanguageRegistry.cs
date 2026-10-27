using Hypershell.Models;

namespace Hypershell.Services;

public enum CommandBuilderPrimaryDestination
{
    Terminal,
    Editor
}

public sealed record CommandLanguageDefinition(
    string Id,
    string DisplayName,
    int Order,
    ExecutionTarget ExecutionTarget,
    TerminalShellKind PreferredShellKind,
    bool SupportsTerminalExecution,
    CommandBuilderPrimaryDestination PrimaryDestination,
    string PreferredEditorLanguageId,
    string PipelineHeading,
    string ComposedTextHeading,
    string CompositionUnitSingular,
    string CompositionUnitPlural,
    string WorkflowDescription,
    string RunActionDescription,
    IReadOnlyList<string> Aliases);

/// <summary>
/// Owns the small amount of host policy required to turn an inert Hyperlib
/// language surface into an executable command-builder surface. Content stays
/// data-driven; adding another language does not require another set of
/// Command Builder conditionals.
/// </summary>
public static class CommandLanguageRegistry
{
    public static IReadOnlyList<CommandLanguageDefinition> Definitions { get; } =
    [
        Define(
            "powershell",
            "PowerShell",
            10,
            ExecutionTarget.PowerShell,
            TerminalShellKind.PowerShell,
            supportsTerminalExecution: true,
            CommandBuilderPrimaryDestination.Terminal,
            "powershell",
            "PIPELINE",
            "COMPOSED COMMAND",
            "command",
            "commands",
            "Build PowerShell commands and pipelines, then review them in an editor or stage and run them in a compatible PowerShell terminal.",
            "Run the composition in a compatible PowerShell terminal.",
            "pwsh"),
        Define(
            "csharp",
            "C#",
            20,
            ExecutionTarget.CSharp,
            TerminalShellKind.CSharp,
            supportsTerminalExecution: true,
            CommandBuilderPrimaryDestination.Terminal,
            "csharp",
            "SCRIPT",
            "COMPOSED C# SOURCE",
            "statement",
            "statements",
            "Compose C# statements from language patterns, .NET APIs, and dotnet tools; insert the source into an editor or compile it with the local .NET SDK.",
            "Compile and run the composition as an isolated C# program with the local .NET SDK.",
            "c-sharp",
            "dotnet",
            ".net"),
        Define(
            "cpp",
            "C++",
            25,
            ExecutionTarget.External,
            TerminalShellKind.Cpp,
            supportsTerminalExecution: true,
            CommandBuilderPrimaryDestination.Editor,
            "cpp",
            "SOURCE BLOCKS",
            "COMPOSED C++ SOURCE",
            "source block",
            "source blocks",
            "Assemble C++20 source blocks for an editor; when a compiler is available, Hypershell can also build and run the composition as an isolated snippet.",
            "Compile and run the composition as an isolated C++20 program.",
            "cxx",
            "cplusplus"),
        Define(
            "python",
            "Python",
            30,
            ExecutionTarget.External,
            TerminalShellKind.Python,
            supportsTerminalExecution: true,
            CommandBuilderPrimaryDestination.Terminal,
            "python",
            "SCRIPT",
            "COMPOSED PYTHON SOURCE",
            "statement",
            "statements",
            "Compose Python statements for a persistent interpreter session, or insert the source into a Python document.",
            "Run the composition in the persistent Python session.",
            "py"),
        Define(
            "javascript",
            "JavaScript",
            40,
            ExecutionTarget.External,
            TerminalShellKind.NodeJs,
            supportsTerminalExecution: true,
            CommandBuilderPrimaryDestination.Terminal,
            "javascript",
            "SCRIPT",
            "COMPOSED JAVASCRIPT SOURCE",
            "statement",
            "statements",
            "Compose JavaScript statements for a persistent Node.js session, or insert the source into a JavaScript document.",
            "Run the composition in the persistent Node.js session.",
            "js",
            "node",
            "nodejs",
            "node.js"),
        Define(
            "typescript",
            "TypeScript",
            50,
            ExecutionTarget.External,
            TerminalShellKind.TypeScript,
            supportsTerminalExecution: true,
            CommandBuilderPrimaryDestination.Editor,
            "typescript",
            "SCRIPT",
            "COMPOSED TYPESCRIPT SOURCE",
            "statement",
            "statements",
            "Assemble TypeScript source for an editor; a compatible Node.js runtime can execute erasable types without performing a full type check.",
            "Run the composition with Node.js native type stripping; this does not type-check the source.",
            "ts"),
        Define(
            "java",
            "Java",
            60,
            ExecutionTarget.External,
            TerminalShellKind.Java,
            supportsTerminalExecution: true,
            CommandBuilderPrimaryDestination.Editor,
            "java",
            "SOURCE BLOCKS",
            "COMPOSED JAVA SOURCE",
            "source block",
            "source blocks",
            "Assemble Java source blocks for an editor; a local JDK 17 or newer can compile and run each composition as an isolated program.",
            "Compile and run the composition as an isolated Java program with JDK 17 or newer.",
            "jdk"),
        Define(
            "html",
            "HTML",
            70,
            ExecutionTarget.External,
            TerminalShellKind.External,
            supportsTerminalExecution: false,
            CommandBuilderPrimaryDestination.Editor,
            "html",
            "DOCUMENT BLOCKS",
            "COMPOSED MARKUP",
            "element",
            "elements",
            "Assemble HTML elements and insert the markup into an HTML document. HTML composition is intentionally editor-only.",
            string.Empty,
            "hypertext-markup-language"),
        Define(
            "css",
            "CSS",
            80,
            ExecutionTarget.External,
            TerminalShellKind.External,
            supportsTerminalExecution: false,
            CommandBuilderPrimaryDestination.Editor,
            "css",
            "STYLESHEET",
            "COMPOSED STYLES",
            "rule",
            "rules",
            "Assemble CSS rules and insert the styles into a CSS or SCSS document. CSS composition is intentionally editor-only.",
            string.Empty,
            "cascading-style-sheets"),
        Define(
            "web",
            "Web APIs",
            90,
            ExecutionTarget.External,
            TerminalShellKind.External,
            supportsTerminalExecution: false,
            CommandBuilderPrimaryDestination.Editor,
            "html",
            "SOURCE BLOCKS",
            "COMPOSED WEB SOURCE",
            "API snippet",
            "API snippets",
            "Compose browser API snippets and insert them into compatible HTML, JavaScript, or TypeScript documents.",
            string.Empty,
            "browser",
            "browser-apis",
            "dom")
    ];

    public static IReadOnlyList<string> ProfileLanguageOptions => Definitions
        .Where(definition => definition.SupportsTerminalExecution)
        .Select(definition => definition.DisplayName)
        .ToList();

    public static string NormalizeDisplayName(string? language)
    {
        var trimmed = language?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return TryGet(trimmed, out var definition)
            ? definition.DisplayName
            : trimmed;
    }

    public static string NormalizeId(string? language)
    {
        var trimmed = language?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return TryGet(trimmed, out var definition)
            ? definition.Id
            : trimmed.ToLowerInvariant();
    }

    public static bool TryGet(
        string? language,
        out CommandLanguageDefinition definition)
    {
        var key = NormalizeLookupKey(language);
        definition = Definitions.FirstOrDefault(candidate =>
            string.Equals(
                NormalizeLookupKey(candidate.Id),
                key,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                NormalizeLookupKey(candidate.DisplayName),
                key,
                StringComparison.OrdinalIgnoreCase)
            || candidate.Aliases.Any(alias => string.Equals(
                NormalizeLookupKey(alias),
                key,
                StringComparison.OrdinalIgnoreCase)))!;
        return definition is not null;
    }

    public static bool CanExecute(
        string? language,
        ExecutionTarget executionTarget,
        TerminalShellKind shellKind)
    {
        if (!TryGet(language, out var definition)
            || !definition.SupportsTerminalExecution
            || definition.ExecutionTarget != executionTarget)
        {
            return false;
        }

        return definition.Id switch
        {
            "powershell" => shellKind is TerminalShellKind.PowerShell
                or TerminalShellKind.WindowsPowerShell
                or TerminalShellKind.DeveloperPowerShell,
            _ => definition.PreferredShellKind == shellKind
        };
    }

    public static bool CanInsertIntoEditor(
        string? commandLanguage,
        string? editorLanguageId)
    {
        var commandId = NormalizeId(commandLanguage);
        var editorId = EditorLanguageCatalog.GetLanguage(editorLanguageId).Id;
        return commandId switch
        {
            "web" => editorId is "html" or "javascript" or "typescript",
            "css" => editorId is "css" or "scss",
            _ => string.Equals(
                commandId,
                editorId,
                StringComparison.OrdinalIgnoreCase)
        };
    }

    public static string GetPreferredEditorLanguageId(string? language) =>
        TryGet(language, out var definition)
            ? definition.PreferredEditorLanguageId
            : NormalizeId(language);

    public static IReadOnlyList<string> OrderLanguages(
        IEnumerable<string?> languages)
    {
        var normalized = languages
            .Select(NormalizeDisplayName)
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return normalized
            .OrderBy(language => TryGet(language, out var definition)
                ? definition.Order
                : int.MaxValue)
            .ThenBy(language => language, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string GetRuntimeDisplayName(TerminalShellKind shellKind) =>
        shellKind switch
        {
            TerminalShellKind.PowerShell => "PowerShell",
            TerminalShellKind.WindowsPowerShell => "Windows PowerShell",
            TerminalShellKind.DeveloperPowerShell => "Developer PowerShell",
            TerminalShellKind.CSharp => "C#",
            TerminalShellKind.Python => "Python",
            TerminalShellKind.NodeJs => "Node.js",
            TerminalShellKind.TypeScript => "TypeScript",
            TerminalShellKind.Java => "Java",
            TerminalShellKind.Cpp => "C++",
            _ => shellKind.ToString()
        };

    private static CommandLanguageDefinition Define(
        string id,
        string displayName,
        int order,
        ExecutionTarget executionTarget,
        TerminalShellKind preferredShellKind,
        bool supportsTerminalExecution,
        CommandBuilderPrimaryDestination primaryDestination,
        string preferredEditorLanguageId,
        string pipelineHeading,
        string composedTextHeading,
        string compositionUnitSingular,
        string compositionUnitPlural,
        string workflowDescription,
        string runActionDescription,
        params string[] aliases) =>
        new(
            id,
            displayName,
            order,
            executionTarget,
            preferredShellKind,
            supportsTerminalExecution,
            primaryDestination,
            preferredEditorLanguageId,
            pipelineHeading,
            composedTextHeading,
            compositionUnitSingular,
            compositionUnitPlural,
            workflowDescription,
            runActionDescription,
            aliases);

    private static string NormalizeLookupKey(string? value) =>
        (value?.Trim() ?? string.Empty)
        .Replace(" ", string.Empty, StringComparison.Ordinal)
        .Replace("-", string.Empty, StringComparison.Ordinal)
        .Replace(".", string.Empty, StringComparison.Ordinal)
        .ToLowerInvariant();
}
