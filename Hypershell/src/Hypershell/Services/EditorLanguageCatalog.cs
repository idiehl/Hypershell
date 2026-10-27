using Hypershell.Models;
using System.IO;

namespace Hypershell.Services;

public static class EditorLanguageCatalog
{
    public static IReadOnlyList<EditorLanguageDefinition> Languages { get; } =
    [
        Language("plaintext", "Plain text", ".txt", 4, true, true,
            ".txt", ".log", ".csv", ".tsv", ".env", ".conf", ".config",
            ".properties", ".editorconfig", ".gitignore", ".dockerignore"),
        Language("diff", "Diff / Patch", ".diff", 4, true, false,
            ".diff", ".patch"),
        Language("csharp", "C#", ".cs", 4, true, false, ".cs", ".csx"),
        Language("powershell", "PowerShell", ".ps1", 4, true, false, ".ps1", ".psm1", ".psd1"),
        Language("python", "Python", ".py", 4, true, false, ".py"),
        Language("javascript", "JavaScript", ".js", 2, true, false, ".js", ".jsx", ".mjs", ".cjs"),
        Language("typescript", "TypeScript", ".ts", 2, true, false, ".ts", ".tsx"),
        Language("json", "JSON", ".json", 2, true, false, ".json", ".jsonc"),
        Language("html", "HTML", ".html", 2, true, true, ".html", ".htm"),
        Language("css", "CSS", ".css", 2, true, false, ".css"),
        Language("scss", "SCSS", ".scss", 2, true, false, ".scss"),
        Language("markdown", "Markdown", ".md", 2, true, true,
            ".md", ".markdown", ".mdown", ".mkd", ".mdx"),
        Language("yaml", "YAML", ".yaml", 2, true, false, ".yaml", ".yml"),
        Language("xml", "XML / XAML", ".xml", 2, true, true, ".xml", ".xaml"),
        Language("sql", "SQL", ".sql", 2, true, false, ".sql"),
        Language("graphql", "GraphQL", ".graphql", 2, true, false, ".graphql", ".gql"),
        Language("protobuf", "Protocol Buffers", ".proto", 2, true, false, ".proto"),
        Language("bicep", "Bicep", ".bicep", 2, true, false, ".bicep"),
        Language("shell", "Shell / Bash", ".sh", 2, true, false, ".sh", ".bash"),
        Language("bat", "Windows batch", ".bat", 4, true, false, ".bat", ".cmd"),
        Language("cpp", "C++", ".cpp", 4, true, false, ".cpp", ".cc", ".cxx", ".h", ".hpp"),
        Language("c", "C", ".c", 4, true, false, ".c"),
        Language("java", "Java", ".java", 4, true, false, ".java"),
        Language("go", "Go", ".go", 4, false, false, ".go"),
        Language("rust", "Rust", ".rs", 4, true, false, ".rs"),
        Language("kotlin", "Kotlin", ".kt", 4, true, false, ".kt", ".kts"),
        Language("swift", "Swift", ".swift", 4, true, false, ".swift"),
        Language("fsharp", "F#", ".fs", 4, true, false, ".fs", ".fsx"),
        Language("vb", "Visual Basic", ".vb", 4, true, false, ".vb"),
        Language("php", "PHP", ".php", 4, true, false, ".php"),
        Language("ruby", "Ruby", ".rb", 2, true, false, ".rb"),
        Language("lua", "Lua", ".lua", 2, true, false, ".lua"),
        Language("dart", "Dart", ".dart", 2, true, false, ".dart"),
        Language("r", "R", ".r", 2, true, false, ".r"),
        Language("ini", "INI / TOML", ".toml", 2, true, false, ".ini", ".cfg", ".toml"),
        Language("dockerfile", "Dockerfile", "Dockerfile", 4, true, false)
    ];

    private static readonly IReadOnlyDictionary<string, EditorLanguageDefinition>
        LanguageById = Languages.ToDictionary(
            language => language.Id,
            StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, EditorLanguageDefinition>
        LanguageByExtension = Languages
            .SelectMany(language => language.FileExtensions.Select(extension =>
                new KeyValuePair<string, EditorLanguageDefinition>(extension, language)))
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Value,
                StringComparer.OrdinalIgnoreCase);

    public static string ResolveLanguageId(string? path)
        => ResolveDefinition(path).Id;

    public static EditorLanguageDefinition ResolveDefinition(string? path)
    {
        var fileName = string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.GetFileName(path);
        if (string.Equals(fileName, "Dockerfile", StringComparison.OrdinalIgnoreCase))
        {
            return GetLanguage("dockerfile");
        }

        var extension = string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.GetExtension(path);
        return LanguageByExtension.GetValueOrDefault(extension, Languages[0]);
    }

    public static EditorLanguageDefinition GetLanguage(string? languageId) =>
        string.IsNullOrWhiteSpace(languageId)
            ? Languages[0]
            : LanguageById.GetValueOrDefault(languageId, Languages[0]);

    public static DocumentEditorSettings CreateDefaultSettings(
        string languageId,
        bool detectIndentation = false)
    {
        var language = GetLanguage(languageId);
        return new DocumentEditorSettings
        {
            LanguageId = language.Id,
            TabSize = language.DefaultTabSize,
            InsertSpaces = language.DefaultInsertSpaces,
            DetectIndentation = detectIndentation,
            WordWrap = language.DefaultWordWrap
        };
    }

    private static EditorLanguageDefinition Language(
        string id,
        string displayName,
        string defaultExtension,
        int defaultTabSize,
        bool defaultInsertSpaces,
        bool defaultWordWrap,
        params string[] fileExtensions) =>
        new(
            id,
            displayName,
            defaultExtension,
            defaultTabSize,
            defaultInsertSpaces,
            defaultWordWrap,
            fileExtensions);
}
