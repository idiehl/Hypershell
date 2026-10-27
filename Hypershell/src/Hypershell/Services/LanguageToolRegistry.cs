using Hypershell.Models;

namespace Hypershell.Services;

public static class LanguageToolRegistry
{
    public static IReadOnlyList<LanguageToolDefinition> Definitions { get; } =
        CreateDefinitions();

    private static IReadOnlyList<LanguageToolDefinition> CreateDefinitions()
    {
        var editorLanguages = EditorLanguageCatalog.Languages
            .Select(language => language.Id)
            .ToList();

        return
        [
            BuiltIn(
                "runtime.powershell.embedded",
                "Embedded PowerShell",
                LanguageToolKind.Runtime,
                ["powershell"],
                "The PowerShell engine hosted by Hypershell."),
            Executable(
                "runtime.dotnet",
                ".NET SDK",
                LanguageToolKind.Runtime,
                ["csharp", "fsharp", "vb"],
                ["dotnet"],
                "Installed .NET SDK command-line host."),
            Executable(
                "runtime.python",
                "Python",
                LanguageToolKind.Runtime,
                ["python"],
                ["python", "python3"],
                "Installed Python interpreter."),
            Executable(
                "runtime.node",
                "Node.js",
                LanguageToolKind.Runtime,
                ["javascript", "typescript"],
                ["node"],
                "Installed Node.js runtime."),
            Executable(
                "runtime.go",
                "Go",
                LanguageToolKind.Runtime,
                ["go"],
                ["go"],
                "Installed Go toolchain."),
            Executable(
                "runtime.rust",
                "Rust compiler",
                LanguageToolKind.Runtime,
                ["rust"],
                ["rustc"],
                "Installed Rust compiler."),
            Executable(
                "runtime.java",
                "Java",
                LanguageToolKind.Runtime,
                ["java", "kotlin"],
                ["java"],
                "Installed Java runtime."),
            Executable(
                "compiler.javac",
                "Java compiler",
                LanguageToolKind.Compiler,
                ["java"],
                ["javac"],
                "Installed javac compiler used for isolated document and snippet builds."),
            Executable(
                "compiler.cpp.clang",
                "Clang C++ compiler",
                LanguageToolKind.Compiler,
                ["cpp"],
                ["clang++"],
                "Installed Clang C++ compiler used for isolated C++20 builds."),
            Executable(
                "compiler.cpp.gcc",
                "GNU C++ compiler",
                LanguageToolKind.Compiler,
                ["cpp"],
                ["g++"],
                "Installed GNU C++ compiler used for isolated C++20 builds."),
            Executable(
                "compiler.cpp.msvc",
                "Microsoft C++ compiler",
                LanguageToolKind.Compiler,
                ["cpp"],
                ["cl"],
                "MSVC compiler available in the current developer environment."),
            Executable(
                "shell.jshell",
                "JShell",
                LanguageToolKind.InteractiveShell,
                ["java"],
                ["jshell"],
                "Installed Java interactive shell. Detected for future exploratory-session integration."),
            Executable(
                "runtime.clang",
                "Clang",
                LanguageToolKind.Runtime,
                ["c", "cpp"],
                ["clang"],
                "Installed Clang compiler."),
            Executable(
                "runtime.ruby",
                "Ruby",
                LanguageToolKind.Runtime,
                ["ruby"],
                ["ruby"],
                "Installed Ruby interpreter."),
            Executable(
                "runtime.php",
                "PHP",
                LanguageToolKind.Runtime,
                ["php"],
                ["php"],
                "Installed PHP interpreter."),
            Executable(
                "runtime.dart",
                "Dart",
                LanguageToolKind.Runtime,
                ["dart"],
                ["dart"],
                "Installed Dart SDK."),

            BuiltIn(
                "formatter.monaco",
                "Monaco editor formatter",
                LanguageToolKind.Formatter,
                editorLanguages,
                "The in-editor formatting fallback. It never starts an external process."),
            Formatter(
                "formatter.ruff",
                "Ruff",
                ["python"],
                ["ruff"],
                ["format", "--stdin-filename", "{filePath}", "-"],
                priority: 10),
            Formatter(
                "formatter.black",
                "Black",
                ["python"],
                ["black"],
                ["--quiet", "--stdin-filename", "{filePath}", "-"],
                priority: 20),
            Formatter(
                "formatter.gofmt",
                "gofmt",
                ["go"],
                ["gofmt"],
                [],
                priority: 10),
            Formatter(
                "formatter.rustfmt",
                "rustfmt",
                ["rust"],
                ["rustfmt"],
                [],
                priority: 10),
            Formatter(
                "formatter.clang-format",
                "clang-format",
                ["c", "cpp"],
                ["clang-format"],
                ["--assume-filename={filePath}"],
                priority: 10),
            Formatter(
                "formatter.shfmt",
                "shfmt",
                ["shell"],
                ["shfmt"],
                [],
                priority: 10),

            LanguageServer(
                "lsp.csharp",
                "C# language server",
                ["csharp"],
                ["csharp-ls"],
                ["--stdio"]),
            LanguageServer(
                "lsp.python",
                "Pyright language server",
                ["python"],
                ["pyright-langserver"],
                ["--stdio"]),
            LanguageServer(
                "lsp.typescript",
                "TypeScript language server",
                ["javascript", "typescript"],
                ["typescript-language-server"],
                ["--stdio"]),
            LanguageServer(
                "lsp.clangd",
                "clangd",
                ["c", "cpp"],
                ["clangd"],
                []),
            LanguageServer(
                "lsp.go",
                "gopls",
                ["go"],
                ["gopls"],
                ["serve"]),
            LanguageServer(
                "lsp.rust",
                "rust-analyzer",
                ["rust"],
                ["rust-analyzer"],
                [])
        ];
    }

    private static LanguageToolDefinition BuiltIn(
        string id,
        string displayName,
        LanguageToolKind kind,
        IReadOnlyList<string> languageIds,
        string description) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Kind = kind,
            LanguageIds = languageIds,
            IsHostBuiltIn = true,
            AcceptsDocumentOnStandardInput = kind == LanguageToolKind.Formatter,
            Description = description
        };

    private static LanguageToolDefinition Executable(
        string id,
        string displayName,
        LanguageToolKind kind,
        IReadOnlyList<string> languageIds,
        IReadOnlyList<string> executableCandidates,
        string description) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Kind = kind,
            LanguageIds = languageIds,
            ExecutableCandidates = executableCandidates,
            Description = description
        };

    private static LanguageToolDefinition Formatter(
        string id,
        string displayName,
        IReadOnlyList<string> languageIds,
        IReadOnlyList<string> executableCandidates,
        IReadOnlyList<string> arguments,
        int priority) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Kind = LanguageToolKind.Formatter,
            LanguageIds = languageIds,
            ExecutableCandidates = executableCandidates,
            Arguments = arguments,
            Priority = priority,
            AcceptsDocumentOnStandardInput = true,
            Description = "Formats editor text through a bounded stdin/stdout process."
        };

    private static LanguageToolDefinition LanguageServer(
        string id,
        string displayName,
        IReadOnlyList<string> languageIds,
        IReadOnlyList<string> executableCandidates,
        IReadOnlyList<string> arguments) =>
        new()
        {
            Id = id,
            DisplayName = displayName,
            Kind = LanguageToolKind.LanguageServer,
            LanguageIds = languageIds,
            ExecutableCandidates = executableCandidates,
            Arguments = arguments,
            Description = "Detected for the future language-client adapter. It is not started automatically."
        };
}
