using Hypershell.Models;

namespace Hypershell.Services;

/// <summary>
/// Provider-neutral seam for a future in-app agent. The first supported task is
/// contextual command-definition generation, but no vendor or credential model
/// is assumed here.
/// </summary>
public interface ICommandDefinitionAgent
{
    string DisplayName { get; }
    bool IsConfigured { get; }
    string StatusMessage { get; }

    Task<CommandDefinitionAgentResult> GenerateDefinitionAsync(
        CommandDefinitionAgentContext context,
        CancellationToken cancellationToken = default);
}

public sealed class UnconfiguredCommandDefinitionAgent : ICommandDefinitionAgent
{
    public string DisplayName => "Hypershell AI";
    public bool IsConfigured => false;
    public string StatusMessage =>
        "Provider not configured. Choose a provider, model, privacy policy, and credential flow before enabling generation.";

    public Task<CommandDefinitionAgentResult> GenerateDefinitionAsync(
        CommandDefinitionAgentContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new CommandDefinitionAgentResult(
            false,
            string.Empty,
            string.Empty,
            DisplayName,
            StatusMessage));
}
