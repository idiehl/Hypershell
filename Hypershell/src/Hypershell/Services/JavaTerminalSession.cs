using Hypershell.Models;

namespace Hypershell.Services;

public sealed class JavaTerminalSession : ITerminalSession
{
    private readonly JavaCompiledLanguageAdapter _adapter = new();
    private readonly DiscoveredCompiledLanguageToolchain _toolchain;

    public JavaTerminalSession(TerminalProfile profile)
    {
        _toolchain = JavaCompiledLanguageAdapter.CreateToolchain(profile);
    }

    public async Task<TerminalExecutionResult> ExecuteAsync(
        string command,
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var execution = await _adapter.ExecuteSnippetAsync(
            command,
            workingDirectory,
            _toolchain,
            cancellationToken);
        var result = new TerminalExecutionResult
        {
            WorkingDirectory = execution.WorkingDirectory
        };
        foreach (var line in execution.Output)
        {
            result.Output.Add(line);
        }

        foreach (var warning in execution.Warnings)
        {
            result.Warnings.Add(warning);
        }

        foreach (var diagnostic in execution.Diagnostics)
        {
            result.Errors.Add(diagnostic);
        }

        if (!execution.Succeeded && result.Errors.Count == 0)
        {
            result.Errors.Add(execution.Message);
        }

        return result;
    }

    public void Dispose()
    {
    }
}
