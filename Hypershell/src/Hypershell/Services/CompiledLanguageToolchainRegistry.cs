using Hypershell.Models;

namespace Hypershell.Services;

public static class CompiledLanguageToolchainRegistry
{
    public static IReadOnlyList<CompiledLanguageToolchainDefinition> Definitions { get; } =
    [
        new()
        {
            Id = "java-jdk",
            DisplayName = "Java JDK 17+",
            LanguageId = "java",
            CompilerToolId = "compiler.javac",
            RuntimeToolId = "runtime.java"
        },
        new()
        {
            Id = "cpp-clang",
            DisplayName = "Clang C++20",
            LanguageId = "cpp",
            CompilerToolId = "compiler.cpp.clang",
            RuntimeToolId = string.Empty,
            RequiresRuntime = false
        },
        new()
        {
            Id = "cpp-gcc",
            DisplayName = "GNU C++20",
            LanguageId = "cpp",
            CompilerToolId = "compiler.cpp.gcc",
            RuntimeToolId = string.Empty,
            RequiresRuntime = false
        },
        new()
        {
            Id = "cpp-msvc",
            DisplayName = "Microsoft C++20",
            LanguageId = "cpp",
            CompilerToolId = "compiler.cpp.msvc",
            RuntimeToolId = string.Empty,
            RequiresRuntime = false
        }
    ];

    public static bool SupportsLanguage(string? languageId)
    {
        var normalized = EditorLanguageCatalog.GetLanguage(languageId).Id;
        return Definitions.Any(definition => string.Equals(
            definition.LanguageId,
            normalized,
            StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class CompiledLanguageToolchainDiscoveryService
{
    private readonly IReadOnlyList<CompiledLanguageToolchainDefinition> _definitions;

    public CompiledLanguageToolchainDiscoveryService(
        IReadOnlyList<CompiledLanguageToolchainDefinition>? definitions = null)
    {
        _definitions = definitions ?? CompiledLanguageToolchainRegistry.Definitions;
    }

    public IReadOnlyList<DiscoveredCompiledLanguageToolchain> Discover(
        LanguageToolCatalogSnapshot catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return _definitions
            .Select(definition => Discover(definition, catalog))
            .ToList();
    }

    public DiscoveredCompiledLanguageToolchain? FindForLanguage(
        string? languageId,
        LanguageToolCatalogSnapshot catalog)
    {
        var normalized = EditorLanguageCatalog.GetLanguage(languageId).Id;
        var candidates = _definitions
            .Where(candidate => string.Equals(
                candidate.LanguageId,
                normalized,
                StringComparison.OrdinalIgnoreCase))
            .Select(definition => Discover(definition, catalog))
            .ToList();
        return candidates.FirstOrDefault(candidate => candidate.IsAvailable)
               ?? candidates.FirstOrDefault();
    }

    private static DiscoveredCompiledLanguageToolchain Discover(
        CompiledLanguageToolchainDefinition definition,
        LanguageToolCatalogSnapshot catalog)
    {
        var compiler = catalog.Tools.FirstOrDefault(tool => string.Equals(
            tool.Definition.Id,
            definition.CompilerToolId,
            StringComparison.OrdinalIgnoreCase));
        var runtime = definition.RequiresRuntime
            ? catalog.Tools.FirstOrDefault(tool => string.Equals(
                tool.Definition.Id,
                definition.RuntimeToolId,
                StringComparison.OrdinalIgnoreCase))
            : null;
        var missing = new List<string>();
        if (compiler?.IsAvailable != true)
        {
            missing.Add(compiler?.Definition.DisplayName ?? definition.CompilerToolId);
        }

        if (definition.RequiresRuntime && runtime?.IsAvailable != true)
        {
            missing.Add(runtime?.Definition.DisplayName ?? definition.RuntimeToolId);
        }

        return new DiscoveredCompiledLanguageToolchain
        {
            Definition = definition,
            Compiler = compiler,
            Runtime = runtime,
            StatusMessage = missing.Count == 0
                ? definition.RequiresRuntime
                    ? $"{definition.DisplayName} compiler and runtime detected."
                    : $"{definition.DisplayName} compiler detected."
                : $"Missing required tool(s): {string.Join(", ", missing)}."
        };
    }
}
