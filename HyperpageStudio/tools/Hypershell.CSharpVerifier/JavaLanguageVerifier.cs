using Hypershell.Models;
using Hypershell.Services;
using Hypershell.ViewModels;
using System.Text;

internal static class JavaLanguageVerifier
{
    public static async Task VerifyAsync(string verifierRoot)
    {
        var root = Path.Combine(verifierRoot, "java-language");
        var sourceRoot = Path.Combine(root, "project", "src", "demo");
        Directory.CreateDirectory(sourceRoot);
        var compiledTempRoot = Path.Combine(
            Path.GetTempPath(),
            "Hypershell",
            "compiled",
            "java");
        var existingCompilationDirectories = Directory.Exists(compiledTempRoot)
            ? Directory.EnumerateDirectories(compiledTempRoot)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var helperPath = Path.Combine(
            AppContext.BaseDirectory,
            "Hypershell.CSharpVerifier.exe");
        Require(File.Exists(helperPath), "The Java verifier helper executable was not found.");
        var helperName = Path.GetFileName(helperPath);
        var helperDirectory = Path.GetDirectoryName(helperPath)!;
        var definitions = new List<LanguageToolDefinition>
        {
            new()
            {
                Id = "compiler.javac",
                DisplayName = "Synthetic javac",
                Kind = LanguageToolKind.Compiler,
                LanguageIds = ["java"],
                ExecutableCandidates = [helperName],
                Arguments = ["--fake-java-compiler"]
            },
            new()
            {
                Id = "runtime.java",
                DisplayName = "Synthetic Java runtime",
                Kind = LanguageToolKind.Runtime,
                LanguageIds = ["java"],
                ExecutableCandidates = [helperName],
                Arguments = ["--fake-java-runtime"]
            }
        };
        var catalog = new LanguageToolDiscoveryService(definitions)
            .Discover(helperDirectory);
        var toolchain = new CompiledLanguageToolchainDiscoveryService()
            .FindForLanguage("java", catalog);
        Require(
            toolchain is { IsAvailable: true }
            && catalog.AvailableCompilerCount == 1
            && catalog.AvailableRuntimeCount == 1,
            "The compiled-language registry did not assemble a Java compiler/runtime pair.");

        var originalPath = Path.Combine(sourceRoot, "Main.java");
        await File.WriteAllTextAsync(
            originalPath,
            "package demo; class Main { } // saved copy",
            new UTF8Encoding(false));
        var liveSource =
            "package demo;\n" +
            "class Main {\n" +
            "    public static void main(String[] args) {\n" +
            "        System.out.println(\"live editor copy\");\n" +
            "    }\n" +
            "}\n";
        var service = new CompiledDocumentExecutionService();
        var success = await service.ExecuteAsync(
            new CompiledLanguageExecutionRequest
            {
                LanguageId = "java",
                FilePath = originalPath,
                Content = liveSource,
                WorkingDirectory = Path.Combine(root, "project")
            },
            catalog);
        Require(
            success.Succeeded
            && success.EntryPoint == "demo.Main"
            && success.Output.Contains("JAVA_FAKE_OK|demo.Main")
            && success.Warnings.Contains("synthetic javac warning"),
            $"The Java document adapter failed its compile/run path: {success.Message}");
        Require(
            (await File.ReadAllTextAsync(originalPath)).Contains(
                "saved copy",
                StringComparison.Ordinal)
            && !Directory.EnumerateFiles(
                    Path.Combine(root, "project"),
                    "*.class",
                    SearchOption.AllDirectories)
                .Any(),
            "Java execution modified source content or emitted class files into the project.");

        var compilationFailure = await service.ExecuteAsync(
            new CompiledLanguageExecutionRequest
            {
                LanguageId = "java",
                FilePath = originalPath,
                Content = "package demo;\nclass Main { COMPILE_ERROR }",
                WorkingDirectory = root
            },
            catalog);
        Require(
            compilationFailure.Outcome == CompiledLanguageExecutionOutcome.CompilationFailed
            && compilationFailure.Diagnostics.Any(line => line.Contains(
                "synthetic compilation failure",
                StringComparison.Ordinal)),
            "Java compiler diagnostics were not returned without launching the program. " +
            $"Outcome={compilationFailure.Outcome}; " +
            $"Message={compilationFailure.Message}; " +
            $"Diagnostics={string.Join(" | ", compilationFailure.Diagnostics)}");

        var runtimeFailure = await service.ExecuteAsync(
            new CompiledLanguageExecutionRequest
            {
                LanguageId = "java",
                FilePath = originalPath,
                Content = "package demo;\nclass Main { /* RUNTIME_ERROR */ }",
                WorkingDirectory = root
            },
            catalog);
        Require(
            runtimeFailure.Outcome == CompiledLanguageExecutionOutcome.RuntimeFailed
            && runtimeFailure.Diagnostics.Contains("synthetic Java runtime failure"),
            "A Java runtime failure was not distinguished from compilation failure.");

        var unavailable = await service.ExecuteAsync(
            new CompiledLanguageExecutionRequest
            {
                LanguageId = "java",
                FilePath = originalPath,
                Content = liveSource,
                WorkingDirectory = root
            },
            new LanguageToolCatalogSnapshot());
        Require(
            unavailable.Outcome == CompiledLanguageExecutionOutcome.Unavailable
            && unavailable.Message.Contains("Missing required tool", StringComparison.Ordinal),
            "A missing JDK did not produce a capability-gated result.");

        var snippet = await new JavaCompiledLanguageAdapter().ExecuteSnippetAsync(
            "System.out.println(6 * 7);",
            root,
            toolchain!);
        Require(
            snippet.Succeeded
            && snippet.EntryPoint == "HypershellSnippet"
            && snippet.Output.Contains("JAVA_FAKE_OK|HypershellSnippet"),
            "The Java command-terminal statement wrapper did not use the compiled adapter.");

        var templates = DocumentTemplateCatalog.GetTemplates("java");
        var consoleTemplate = templates.Single(template => template.IsDefault);
        var recordTemplate = templates.Single(template => template.Id == "java-record-model");
        Require(
            templates.Count == 3
            && consoleTemplate.Id == "java-console-app"
            && consoleTemplate.Content.Contains("static void main", StringComparison.Ordinal)
            && recordTemplate.Content.Contains("record Task", StringComparison.Ordinal),
            "Java document templates do not cover a console entry point and Java 17 records.");

        var document = DocumentTabViewModel.CreateUntitled(
            new NewDocumentRequest(
                "Main.java",
                EditorLanguageCatalog.CreateDefaultSettings("java"),
                InitialContent: consoleTemplate.Content,
                TemplateId: consoleTemplate.Id),
            new TextDocumentService());
        var savedDocumentPath = Path.Combine(root, "document-workflow", "Main.java");
        await document.SaveAsync(savedDocumentPath);
        Require(
            document.CanRunDocument
            && document.HasFilePath
            && !document.HasUnsavedChanges
            && File.Exists(savedDocumentPath),
            "A Java starter document did not become a runnable saved editor document.");

        Require(
            !Directory.Exists(compiledTempRoot)
            || !Directory.EnumerateDirectories(compiledTempRoot)
                .Except(existingCompilationDirectories, StringComparer.OrdinalIgnoreCase)
                .Any(),
            "Java execution left an isolated compilation directory behind.");

        Console.WriteLine(
            "JAVA_COMPILED_LANGUAGE_OK|toolchain|document|snippet|diagnostics|isolated-output");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
