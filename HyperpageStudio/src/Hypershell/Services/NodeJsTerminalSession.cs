using Hypershell.Models;

namespace Hypershell.Services;

public sealed class NodeJsTerminalSession : NodeReplTerminalSession
{
    public NodeJsTerminalSession(
        TerminalProfile profile,
        string initialWorkingDirectory)
        : base(profile, initialWorkingDirectory)
    {
    }

    protected override string BuildExecutableSourceExpression(
        string sourceIdentifier) => sourceIdentifier;
}
