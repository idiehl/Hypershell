namespace Hypershell.Models;

public sealed record CommandGuideContent(
    string Summary,
    string Definition,
    string ActionName,
    string ActionDescription,
    string ObjectName,
    string ObjectDescription,
    string Syntax,
    string SourceLabel,
    string SourceUrl,
    bool UsesParameterSets,
    bool IsFallback,
    IReadOnlyList<CommandUsageExample> Examples,
    string SyntaxLanguage = "C#")
{
    public bool HasExamples => Examples.Count > 0;
    public bool HasActionDescription =>
        !string.IsNullOrWhiteSpace(ActionDescription);
    public bool HasObjectDescription =>
        !string.IsNullOrWhiteSpace(ObjectDescription);
    public string ActionHeading => string.IsNullOrWhiteSpace(ActionName)
        ? "WHAT THE ACTION DOES"
        : $"ACTION · {ActionName.ToUpperInvariant()}";
    public string ObjectHeading => string.IsNullOrWhiteSpace(ObjectName)
        ? "WHAT THE OBJECT IS"
        : $"OBJECT · {ObjectName.ToUpperInvariant()}";
    public string SyntaxHeading => UsesParameterSets
        ? "VALID SYNTAX · PARAMETER SETS"
        : "SYNTAX TEMPLATE";
    public string SyntaxExplanation => UsesParameterSets
        ? "Each block is a different valid combination of parameters—not a separate usage example. Use one parameter set at a time."
        : $"Replace each angle-bracket placeholder with a {SyntaxLanguage} " +
          "value or expression of the indicated type.";
    public bool HasDistinctDefinition =>
        !string.IsNullOrWhiteSpace(Definition)
        && !string.Equals(
            Summary.Trim(),
            Definition.Trim(),
            StringComparison.OrdinalIgnoreCase);
}

public sealed class PowerShellGuideCatalog
{
    public string SourceLabel { get; set; } = string.Empty;
    public string SourceRepository { get; set; } = string.Empty;
    public string SourceCommit { get; set; } = string.Empty;
    public string ContentLicense { get; set; } = string.Empty;
    public Dictionary<string, string> Actions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Objects { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PowerShellCommandGuideDefinition> Commands { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PowerShellCommandGuideDefinition
{
    public string Summary { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
    public string Syntax { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public List<CommandUsageExample> Examples { get; set; } = [];
}

public sealed record CommandDefinitionAgentParameter(
    string Name,
    string TypeName,
    string Description,
    bool IsRequired,
    bool IsSwitch);

public sealed record CommandDefinitionAgentContext(
    string Term,
    string Language,
    string Library,
    string Category,
    string CommandType,
    string Syntax,
    string ExistingDefinition,
    string? SelectedAction,
    string? SelectedObject,
    string CommandProfile,
    IReadOnlyList<CommandDefinitionAgentParameter> Parameters);

public sealed record CommandDefinitionAgentResult(
    bool Succeeded,
    string Definition,
    string Summary,
    string SourceLabel,
    string ErrorMessage);
