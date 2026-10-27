using Hypershell.Models;

namespace Hypershell.Services;

public interface ITerminalSession : IDisposable
{
    Task<TerminalExecutionResult> ExecuteAsync(
        string command,
        string? workingDirectory,
        CancellationToken cancellationToken = default);
}
