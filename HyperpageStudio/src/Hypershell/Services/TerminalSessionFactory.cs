using Hypershell.Models;

namespace Hypershell.Services;

public static class TerminalSessionFactory
{
    public static ITerminalSession Create(TerminalProfile profile, string initialWorkingDirectory) =>
        profile.ShellKind switch
        {
            TerminalShellKind.PowerShell or TerminalShellKind.DeveloperPowerShell =>
                new PowerShellTerminalSession(profile),
            TerminalShellKind.WindowsPowerShell =>
                new WindowsPowerShellTerminalSession(profile, initialWorkingDirectory),
            TerminalShellKind.CommandPrompt =>
                new CommandPromptTerminalSession(profile, initialWorkingDirectory),
            TerminalShellKind.WslBash => new BashTerminalSession(profile, initialWorkingDirectory),
            TerminalShellKind.GitBash => new BashTerminalSession(profile, initialWorkingDirectory),
            TerminalShellKind.Python => new PythonTerminalSession(profile, initialWorkingDirectory),
            TerminalShellKind.NodeJs => new NodeJsTerminalSession(profile, initialWorkingDirectory),
            TerminalShellKind.TypeScript =>
                new TypeScriptTerminalSession(profile, initialWorkingDirectory),
            TerminalShellKind.Java => new JavaTerminalSession(profile),
            TerminalShellKind.Cpp => new CppTerminalSession(profile),
            TerminalShellKind.CSharp => new CSharpTerminalSession(profile),
            TerminalShellKind.DockerCli =>
                new DockerCliTerminalSession(profile, initialWorkingDirectory),
            TerminalShellKind.DockerContainer =>
                new DockerContainerTerminalSession(profile),
            TerminalShellKind.MySql => new MySqlTerminalSession(profile, initialWorkingDirectory),
            _ => throw new NotSupportedException(
                $"The terminal profile '{profile.Name}' uses an unsupported shell type.")
        };
}
