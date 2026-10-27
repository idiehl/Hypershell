using Hypershell.Models;
using System.IO;

namespace Hypershell.Services;

public sealed class ExternalDocumentFormatterService
{
    private readonly BoundedProcessRunner _processRunner;
    private readonly TimeSpan _timeout;
    private readonly int _maximumOutputCharacters;

    public ExternalDocumentFormatterService(
        BoundedProcessRunner? processRunner = null,
        TimeSpan? timeout = null,
        int maximumOutputCharacters = 8 * 1024 * 1024)
    {
        _processRunner = processRunner ?? new BoundedProcessRunner();
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        _maximumOutputCharacters = maximumOutputCharacters;
    }

    public async Task<DocumentFormattingResult> FormatAsync(
        DocumentFormattingRequest request,
        LanguageToolCatalogSnapshot catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);

        var tool = catalog.Tools
            .Where(candidate => candidate.IsAvailable
                                && !candidate.Definition.IsHostBuiltIn
                                && candidate.Definition.Kind == LanguageToolKind.Formatter
                                && candidate.Definition.AcceptsDocumentOnStandardInput
                                && candidate.Definition.LanguageIds.Contains(
                                    request.LanguageId,
                                    StringComparer.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.Definition.Priority)
            .ThenBy(candidate => candidate.Definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (tool is null)
        {
            return new DocumentFormattingResult
            {
                Outcome = DocumentFormattingOutcome.Unavailable,
                Message = "No supported external formatter is available for this language."
            };
        }

        var workingDirectory = ResolveWorkingDirectory(request);
        var metadataPath = ResolveMetadataPath(request, workingDirectory);
        var arguments = tool.Definition.Arguments
            .Select(argument => ExpandArgument(argument, metadataPath, workingDirectory))
            .ToList();
        var processResult = await _processRunner.RunAsync(
            new BoundedProcessRequest
            {
                ExecutablePath = tool.ExecutablePath,
                Arguments = arguments,
                StandardInput = request.Content,
                WorkingDirectory = workingDirectory,
                Timeout = _timeout,
                MaximumStandardOutputCharacters = _maximumOutputCharacters
            },
            cancellationToken);

        if (processResult.TimedOut)
        {
            return Result(
                tool,
                DocumentFormattingOutcome.TimedOut,
                processResult.FailureMessage);
        }

        if (processResult.OutputLimitExceeded)
        {
            return Result(
                tool,
                DocumentFormattingOutcome.Rejected,
                processResult.FailureMessage);
        }

        if (!processResult.Started)
        {
            return Result(
                tool,
                DocumentFormattingOutcome.Failed,
                processResult.FailureMessage);
        }

        if (processResult.ExitCode != 0)
        {
            var diagnostic = FirstDiagnostic(processResult.StandardError);
            return Result(
                tool,
                DocumentFormattingOutcome.Failed,
                string.IsNullOrWhiteSpace(diagnostic)
                    ? $"{tool.Definition.DisplayName} exited with code {processResult.ExitCode}."
                    : $"{tool.Definition.DisplayName}: {diagnostic}");
        }

        var formattedText = processResult.StandardOutput;
        if (formattedText.Contains('\0')
            || (request.Content.Length > 0 && formattedText.Length == 0))
        {
            return Result(
                tool,
                DocumentFormattingOutcome.Rejected,
                "The formatter returned invalid or unexpectedly empty text.");
        }

        var outcome = string.Equals(formattedText, request.Content, StringComparison.Ordinal)
            ? DocumentFormattingOutcome.Unchanged
            : DocumentFormattingOutcome.Applied;
        return new DocumentFormattingResult
        {
            Outcome = outcome,
            FormattedText = formattedText,
            FormatterId = tool.Definition.Id,
            FormatterDisplayName = tool.Definition.DisplayName,
            Message = outcome == DocumentFormattingOutcome.Unchanged
                ? $"{tool.Definition.DisplayName} found no formatting changes."
                : $"Formatted with {tool.Definition.DisplayName}."
        };
    }

    private static string ResolveWorkingDirectory(DocumentFormattingRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory)
            && Directory.Exists(request.WorkingDirectory))
        {
            return Path.GetFullPath(request.WorkingDirectory);
        }

        if (!string.IsNullOrWhiteSpace(request.FilePath))
        {
            var directory = Path.GetDirectoryName(request.FilePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                return Path.GetFullPath(directory);
            }
        }

        return Environment.CurrentDirectory;
    }

    private static string ResolveMetadataPath(
        DocumentFormattingRequest request,
        string workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(request.FilePath))
        {
            return Path.GetFullPath(request.FilePath);
        }

        var fileName = Path.GetFileName(request.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "Untitled.txt";
        }

        return Path.Combine(workingDirectory, fileName);
    }

    private static string ExpandArgument(
        string argument,
        string filePath,
        string workingDirectory) =>
        argument
            .Replace("{filePath}", filePath, StringComparison.Ordinal)
            .Replace("{fileName}", Path.GetFileName(filePath), StringComparison.Ordinal)
            .Replace("{workingDirectory}", workingDirectory, StringComparison.Ordinal);

    private static string FirstDiagnostic(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= 512 ? normalized : normalized[..512] + "…";
    }

    private static DocumentFormattingResult Result(
        DiscoveredLanguageTool tool,
        DocumentFormattingOutcome outcome,
        string message) =>
        new()
        {
            Outcome = outcome,
            FormatterId = tool.Definition.Id,
            FormatterDisplayName = tool.Definition.DisplayName,
            Message = message
        };
}
