using System.Text.Json;
using System.IO;

namespace Hypershell.Services;

public sealed class CSharpKeywordDefinition
{
    public string Keyword { get; set; } = string.Empty;
    public string LexicalClass { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string CategoryLabel => Category switch
    {
        "type_declaration" => "Type declarations",
        "built_in_type_or_literal" => "Built-in types and literals",
        "declaration_modifier" => "Declaration modifiers",
        "namespace_or_import" => "Namespaces and imports",
        "object_or_type_expression" => "Object and type expressions",
        "property_or_event_keyword" => "Properties and events",
        "operator_conversion_or_pattern" => "Operators, conversions, and patterns",
        "conditional_control_flow" => "Conditional control flow",
        "loop_or_jump_control_flow" => "Loops, jumps, and iteration",
        "runtime_control_or_safety" => "Runtime control and safety",
        "parameter_reference_or_constraint" => "Parameters, references, and constraints",
        "asynchronous_keyword" => "Asynchronous execution",
        "query_expression_keyword" => "LINQ query expressions",
        "interop_or_unsafe_keyword" => "Interop and unsafe code",
        "compiler_context_keyword" => "Compiler context",
        _ => Category.Replace('_', ' ')
    };

    public string DetailLabel => $"{LexicalClass} · {CategoryLabel}";
}

public static class CSharpKeywordCatalogService
{
    public const int ExpectedKeywordCount = 130;

    public static IReadOnlyList<CSharpKeywordDefinition> Load(
        string? applicationDirectory = null)
    {
        var root = applicationDirectory ?? AppContext.BaseDirectory;
        var path = Path.Combine(root, "Assets", "CSharp", "keywords.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "The C# keyword catalog was not published with Hypershell.",
                path);
        }

        var catalog = JsonSerializer.Deserialize<CSharpKeywordCatalog>(
            File.ReadAllText(path),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? throw new InvalidDataException(
                "The C# keyword catalog is empty or invalid.");

        if (catalog.Count != ExpectedKeywordCount
            || catalog.Keywords.Count != ExpectedKeywordCount
            || catalog.Keywords.Any(keyword =>
                string.IsNullOrWhiteSpace(keyword.Keyword)
                || string.IsNullOrWhiteSpace(keyword.LexicalClass)
                || string.IsNullOrWhiteSpace(keyword.Category)))
        {
            throw new InvalidDataException(
                $"The C# keyword catalog must contain exactly " +
                $"{ExpectedKeywordCount} complete entries.");
        }

        return catalog.Keywords;
    }

    private sealed class CSharpKeywordCatalog
    {
        public int SchemaVersion { get; set; }
        public string Source { get; set; } = string.Empty;
        public int Count { get; set; }
        public List<CSharpKeywordDefinition> Keywords { get; set; } = [];
    }
}
