using Hypershell.Models;

namespace Hypershell.Services;

/// <summary>
/// Executes erasable TypeScript syntax through Node's capability-gated native
/// type stripper. This is snippet execution, not a semantic type-checking
/// replacement for tsc.
/// </summary>
public sealed class TypeScriptTerminalSession : NodeReplTerminalSession
{
    public TypeScriptTerminalSession(
        TerminalProfile profile,
        string initialWorkingDirectory)
        : base(profile, initialWorkingDirectory)
    {
    }

    protected override string SourceFileName => "<hypershell.ts>";

    protected override string BuildExecutableSourceExpression(
        string sourceIdentifier) =>
        "require('node:module').stripTypeScriptTypes(" +
        $"{sourceIdentifier}, {{ mode: 'strip', sourceUrl: '<hypershell.ts>' }})";
}
