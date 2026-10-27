using Hypershell.Infrastructure;
using Hypershell.Models;
using System.Collections.ObjectModel;

namespace Hypershell.ViewModels;

public sealed partial class CommandBuilderViewModel
{
    private bool _isHierarchyLoading;
    private int _hierarchyLoadVersion;

    public ObservableCollection<CommandHierarchyLevelViewModel>
        HierarchyLevels { get; } = [];

    public bool IsHierarchySurface =>
        SelectedBuilderSurface is not null
        && !IsCSharpComposerWorkspace
        && SelectedBuilderSurface.ExecutionTarget != ExecutionTarget.PowerShell;

    public bool HasHierarchyLevels => HierarchyLevels.Count > 0;

    public bool IsHierarchyLoading
    {
        get => _isHierarchyLoading;
        private set
        {
            if (SetProperty(ref _isHierarchyLoading, value))
            {
                OnPropertyChanged(nameof(HierarchyStatus));
            }
        }
    }

    public string HierarchyBreadcrumb
    {
        get
        {
            var selected = HierarchyLevels
                .Select(level => level.SelectedOption?.DisplayName)
                .Where(value => !string.IsNullOrWhiteSpace(value));
            return string.Join(" → ", selected!);
        }
    }

    public string HierarchyStatus => IsHierarchyLoading
        ? $"Loading {SelectedBuilderSurface?.DisplayName}…"
        : HierarchyLevels.Count == 0
            ? "This library does not expose any browsable entries."
            : string.IsNullOrWhiteSpace(HierarchyBreadcrumb)
                ? "Choose the first level to begin browsing this library."
                : HierarchyBreadcrumb;

    private async Task MaterializeBuilderSurfaceAsync(
        BuilderSurfaceOption? surface,
        bool updateStatus = true)
    {
        if (_catalogService is null
            || surface is null
            || string.IsNullOrWhiteSpace(surface.LibraryId))
        {
            return;
        }

        var library = Libraries.FirstOrDefault(candidate => string.Equals(
            candidate.Id,
            surface.LibraryId,
            StringComparison.OrdinalIgnoreCase));
        if (library is null || !library.HasDeferredEntries)
        {
            if (IsHierarchySurface
                && string.Equals(
                    SelectedBuilderSurface?.LibraryId,
                    surface.LibraryId,
                    StringComparison.OrdinalIgnoreCase))
            {
                RefreshHierarchyBrowser();
            }

            return;
        }

        var loadVersion = ++_hierarchyLoadVersion;
        IsHierarchyLoading = true;
        if (updateStatus)
        {
            StatusMessage = $"Loading {library.DisplayName} index…";
        }

        try
        {
            var entries = await _catalogService.LoadLibraryEntriesAsync(library);
            RegisterLibraryCommands(entries);
            if (loadVersion != _hierarchyLoadVersion
                || !string.Equals(
                    SelectedBuilderSurface?.LibraryId,
                    surface.LibraryId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (IsCSharpLanguageSurface)
            {
                RefreshCSharpLanguagePatterns();
            }
            else if (IsCSharpComposerWorkspace)
            {
                RefreshCSharpNamespaceRoot();
            }
            else
            {
                RefreshHierarchyBrowser();
            }
            RefreshSearchResults();
            if (updateStatus)
            {
                StatusMessage =
                    $"Loaded {entries.Count:N0} entries from {library.DisplayName}.";
            }
        }
        catch (Exception exception)
        {
            if (loadVersion == _hierarchyLoadVersion)
            {
                StatusMessage =
                    $"Unable to load {library.DisplayName}: {exception.Message}";
            }
        }
        finally
        {
            if (loadVersion == _hierarchyLoadVersion)
            {
                IsHierarchyLoading = false;
            }
        }
    }

    private void RegisterLibraryCommands(
        IEnumerable<CommandDefinition> entries)
    {
        var known = Commands
            .Select(command => command.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (known.Add(entry.Id))
            {
                Commands.Add(entry);
            }
        }
    }

    private void RefreshHierarchyBrowser()
    {
        ClearHierarchyLevels();
        ClearMatchingCommands();
        if (!IsHierarchySurface
            || SelectedSurfaceLibrary is not { } library
            || library.Commands.Count == 0)
        {
            NotifyHierarchyChanged();
            return;
        }

        AddHierarchyLevel(library.Commands, depth: 0, []);
        NotifyHierarchyChanged();
    }

    private void AddHierarchyLevel(
        IReadOnlyCollection<CommandDefinition> commands,
        int depth,
        IReadOnlyList<CommandHierarchyOption> prefix)
    {
        var options = commands
            .Where(command => PathMatches(command, prefix)
                              && command.BrowsePath.Count > depth)
            .Select(command => command.BrowsePath[depth])
            .GroupBy(
                segment => $"{segment.Kind}\u001F{segment.Name}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(segment => new CommandHierarchyOption(
                segment.Kind,
                segment.Name,
                segment.EffectiveDisplayName))
            .OrderBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (options.Count == 0)
        {
            SetHierarchyMatches(commands.Where(command =>
                PathMatches(command, prefix)));
            return;
        }

        var kinds = options
            .Select(option => option.Kind)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var noun = ResolveHierarchyNoun(kinds);
        var level = new CommandHierarchyLevelViewModel(
            depth,
            $"{depth + 1} · {noun}",
            options);
        level.SelectionChanged += OnHierarchySelectionChanged;
        HierarchyLevels.Add(level);
    }

    private void OnHierarchySelectionChanged(
        object? sender,
        EventArgs eventArgs)
    {
        if (sender is not CommandHierarchyLevelViewModel changed)
        {
            return;
        }

        while (HierarchyLevels.Count > changed.Depth + 1)
        {
            var removed = HierarchyLevels[^1];
            removed.SelectionChanged -= OnHierarchySelectionChanged;
            HierarchyLevels.RemoveAt(HierarchyLevels.Count - 1);
        }

        ClearMatchingCommands();
        var prefix = HierarchyLevels
            .Take(changed.Depth + 1)
            .Select(level => level.SelectedOption)
            .Where(option => option is not null)
            .Cast<CommandHierarchyOption>()
            .ToList();
        if (prefix.Count != changed.Depth + 1
            || SelectedSurfaceLibrary is not { } library)
        {
            NotifyHierarchyChanged();
            return;
        }

        var matching = library.Commands
            .Where(command => PathMatches(command, prefix))
            .ToList();
        if (matching.Any(command => command.BrowsePath.Count > prefix.Count))
        {
            AddHierarchyLevel(matching, prefix.Count, prefix);
        }
        else
        {
            SetHierarchyMatches(matching);
        }

        NotifyHierarchyChanged();
    }

    private void SetHierarchyMatches(IEnumerable<CommandDefinition> commands)
    {
        var matches = commands
            .OrderBy(command => command.OverloadSignature, StringComparer.OrdinalIgnoreCase)
            .ThenBy(command => command.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ReplaceItems(MatchingCommands, matches);
        SelectedCommand = matches.FirstOrDefault();
        OnPropertyChanged(nameof(HasCommandVariants));
    }

    private void OpenHierarchyCommand(CommandDefinition command)
    {
        SelectBuilderSurface(command.CatalogId);
        if (!IsHierarchySurface)
        {
            return;
        }

        RefreshHierarchyBrowser();
        for (var depth = 0;
             depth < command.BrowsePath.Count && depth < HierarchyLevels.Count;
             depth++)
        {
            var segment = command.BrowsePath[depth];
            var option = HierarchyLevels[depth].Options.FirstOrDefault(candidate =>
                string.Equals(candidate.Kind, segment.Kind, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Name, segment.Name, StringComparison.OrdinalIgnoreCase));
            if (option is null)
            {
                break;
            }

            HierarchyLevels[depth].SelectedOption = option;
        }

        SelectedCommand = MatchingCommands.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, command.Id, StringComparison.OrdinalIgnoreCase))
            ?? command;
        StatusMessage = $"Opened {command.DisplayName} from search.";
    }

    private static bool PathMatches(
        CommandDefinition command,
        IReadOnlyList<CommandHierarchyOption> prefix)
    {
        if (command.BrowsePath.Count < prefix.Count)
        {
            return false;
        }

        for (var index = 0; index < prefix.Count; index++)
        {
            if (!string.Equals(
                    command.BrowsePath[index].Kind,
                    prefix[index].Kind,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    command.BrowsePath[index].Name,
                    prefix[index].Name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeHierarchyNoun(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalized)
            ? "ITEM"
            : normalized.Replace("_", " ", StringComparison.Ordinal)
                .ToUpperInvariant();
    }

    private static string ResolveHierarchyNoun(
        IReadOnlyList<string> kinds)
    {
        var normalized = kinds
            .Select(NormalizeHierarchyNoun)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count == 0)
        {
            return "ITEM";
        }

        if (normalized.Count == 1)
        {
            return normalized[0];
        }

        var commonWords = normalized[0]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => normalized.Skip(1).All(kind =>
                kind.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(word, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        if (commonWords.Count > 0)
        {
            return string.Join(' ', commonWords);
        }

        var explicitChoice = string.Join(" OR ", normalized);
        return normalized.Count <= 2 && explicitChoice.Length <= 34
            ? explicitChoice
            : "ITEM";
    }

    private void ClearHierarchyLevels()
    {
        foreach (var level in HierarchyLevels)
        {
            level.SelectionChanged -= OnHierarchySelectionChanged;
        }

        HierarchyLevels.Clear();
    }

    private void NotifyHierarchyChanged()
    {
        OnPropertyChanged(nameof(HasHierarchyLevels));
        OnPropertyChanged(nameof(HierarchyBreadcrumb));
        OnPropertyChanged(nameof(HierarchyStatus));
        OnPropertyChanged(nameof(CommandGuideContextLabel));
        OnPropertyChanged(nameof(FlagsSelectorSummary));
        RefreshStagedStatement();
    }
}

public sealed record CommandHierarchyOption(
    string Kind,
    string Name,
    string DisplayName);

public sealed class CommandHierarchyLevelViewModel : ObservableObject
{
    private CommandHierarchyOption? _selectedOption;

    public CommandHierarchyLevelViewModel(
        int depth,
        string label,
        IEnumerable<CommandHierarchyOption> options)
    {
        Depth = depth;
        Label = label;
        Options = new ObservableCollection<CommandHierarchyOption>(options);
    }

    public int Depth { get; }
    public string Label { get; }
    public ObservableCollection<CommandHierarchyOption> Options { get; }

    public CommandHierarchyOption? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (SetProperty(ref _selectedOption, value))
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? SelectionChanged;
}
