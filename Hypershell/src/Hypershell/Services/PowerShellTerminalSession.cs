using Hypershell.Models;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace Hypershell.Services;

public sealed class PowerShellTerminalSession : ITerminalSession
{
    private readonly Runspace _runspace;
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private string? _workingDirectory;
    private bool _isDisposed;

    public PowerShellTerminalSession(TerminalProfile? profile = null)
    {
        _runspace = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
        try
        {
            _runspace.Open();
            InitializeProfile(profile);
        }
        catch
        {
            _runspace.Dispose();
            throw;
        }
    }

    public async Task<TerminalExecutionResult> ExecuteAsync(
        string command,
        string? workingDirectory,
        CancellationToken cancellationToken = default)
    {
        await _executionGate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => Execute(command, workingDirectory, cancellationToken), CancellationToken.None);
        }
        finally
        {
            _executionGate.Release();
        }
    }

    private TerminalExecutionResult Execute(string command, string? workingDirectory, CancellationToken cancellationToken)
    {
        var result = new TerminalExecutionResult();
        using var shell = PowerShell.Create();
        shell.Runspace = _runspace;

        if (!string.IsNullOrWhiteSpace(workingDirectory)
            && Directory.Exists(workingDirectory)
            && !string.Equals(_workingDirectory, workingDirectory, StringComparison.OrdinalIgnoreCase))
        {
            shell.AddCommand("Set-Location").AddParameter("LiteralPath", workingDirectory);
            shell.AddStatement();
            _workingDirectory = workingDirectory;
        }

        shell.AddScript(command);
        cancellationToken.ThrowIfCancellationRequested();
        var invocation = shell.BeginInvoke();
        using var cancellationRegistration = cancellationToken.Register(
            shell.Stop);

        try
        {
            while (!invocation.IsCompleted)
            {
                invocation.AsyncWaitHandle.WaitOne(50);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var output = shell.EndInvoke(invocation);
            result.Output.AddRange(FormatOutput(output));
            result.WorkingDirectory = GetCurrentWorkingDirectory();
            _workingDirectory = result.WorkingDirectory ?? _workingDirectory;
        }
        catch (RuntimeException exception)
        {
            result.Errors.Add(exception.Message);
        }
        finally
        {
            result.Errors.AddRange(shell.Streams.Error.Select(error => error.ToString()));
            result.Warnings.AddRange(shell.Streams.Warning.Select(warning => warning.Message));
        }

        return result;
    }

    private string? GetCurrentWorkingDirectory()
    {
        using var locationShell = PowerShell.Create();
        locationShell.Runspace = _runspace;
        var location = locationShell.AddCommand("Get-Location").Invoke().FirstOrDefault();
        return location?.Properties["Path"]?.Value?.ToString();
    }

    private void InitializeProfile(TerminalProfile? profile)
    {
        if (string.IsNullOrWhiteSpace(profile?.InitializationCommand))
        {
            return;
        }

        using var shell = PowerShell.Create();
        shell.Runspace = _runspace;
        shell.AddScript(profile.InitializationCommand);
        shell.Invoke();
        if (!shell.HadErrors)
        {
            return;
        }

        var errors = string.Join(
            Environment.NewLine,
            shell.Streams.Error.Select(error => error.ToString()));
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(errors)
                ? $"{profile.Name} could not initialize."
                : errors);
    }

    private static IEnumerable<string> FormatOutput(
        IEnumerable<PSObject> output)
    {
        foreach (var item in output)
        {
            if (item.BaseObject is null)
            {
                continue;
            }

            if (item.BaseObject is string text)
            {
                foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                {
                    yield return line;
                }

                continue;
            }

            var preferredProperties = new[] { "Name", "Id", "Status", "Length", "LastWriteTime", "FullName", "Value" };
            var values = preferredProperties
                .Select(propertyName => item.Properties[propertyName])
                .Where(property => property is not null)
                .Take(4)
                .Select(property => $"{property!.Name}: {FormatPropertyValue(property.Value)}")
                .ToList();

            yield return values.Count > 0
                ? string.Join("  |  ", values)
                : item.ToString();
        }
    }

    private static string FormatPropertyValue(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        return text.Replace("\r", " ").Replace("\n", " ");
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _runspace.Dispose();
        _executionGate.Dispose();
    }
}
