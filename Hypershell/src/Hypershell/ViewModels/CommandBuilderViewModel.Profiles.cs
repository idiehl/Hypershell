using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Services;
using System.Collections.ObjectModel;

namespace Hypershell.ViewModels;

public sealed partial class CommandBuilderViewModel
{
    private static readonly string[] LibraryLanguageOrder =
        CommandLanguageRegistry.Definitions
            .Select(definition => definition.DisplayName)
            .ToArray();

    private LibraryLanguageTabViewModel? _selectedLibraryLanguageTab;
    private string _selectedInputSyntax = "PowerShell";

    public IReadOnlyList<string> InputSyntaxOptions =>
        CommandLanguageRegistry.OrderLanguages(
            CommandLanguageRegistry.Definitions
                .Select(definition => definition.DisplayName)
                .Concat(Libraries.Select(library => library.Language)));

    public string SelectedInputSyntax
    {
        get => _selectedInputSyntax;
        set
        {
            var normalized = NormalizeLanguageName(value);
            if (string.IsNullOrWhiteSpace(normalized)
                || !SetProperty(ref _selectedInputSyntax, normalized))
            {
                return;
            }

            _settings.CommandBuilder.ActiveLanguage = normalized;
            Pipeline.Clear();
            CommandText = string.Empty;
            NotifyPipelineChanged();
            ResetCSharpComposerState(clearStatus: true);
            SynchronizeLibrarySelections();
            OnPropertyChanged(nameof(SelectedEnvironmentLanguage));
            OnPropertyChanged(nameof(InputSyntaxDescription));
            OnPropertyChanged(nameof(ProfileLanguageRule));
            OnPropertyChanged(nameof(IsCSharpComposerWorkspace));
            OnPropertyChanged(nameof(IsCatalogCommandWorkspace));
            OnPropertyChanged(nameof(IsProfileDrivenBuilderWorkspace));
            OnPropertyChanged(nameof(BuilderSurfaceHeading));
            OnPropertyChanged(nameof(BuilderSurfaceToolTip));
            OnPropertyChanged(nameof(SearchSurfaceHeading));
            OnPropertyChanged(nameof(SearchResultSummary));
            NotifyLanguagePresentationChanged();
            OnPropertyChanged(nameof(HasVisibleStatusMessage));
            PersistSettings();
            StatusMessage =
                $"Ready to compose {CompositionUnitPlural} for {normalized}.";
        }
    }

    public string InputSyntaxDescription =>
        CommandLanguageRegistry.TryGet(
            SelectedInputSyntax,
            out var language)
            ? language.WorkflowDescription
            : $"Build source from the installed {SelectedInputSyntax} libraries.";

    public ObservableCollection<LibraryLanguageTabViewModel>
        LibraryLanguageTabs
    { get; } = [];

    public LibraryLanguageTabViewModel? SelectedLibraryLanguageTab
    {
        get => _selectedLibraryLanguageTab;
        set
        {
            if (!SetProperty(ref _selectedLibraryLanguageTab, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanEditSelectedLanguageTab));
            OnPropertyChanged(nameof(SelectedLanguageTabStatus));
            SelectAllLibrariesCommand.RaiseCanExecuteChanged();
            ClearLibrarySelectionCommand.RaiseCanExecuteChanged();
        }
    }

    public string SelectedEnvironmentLanguage =>
        SelectedInputSyntax;

    public string ProfileLanguageRule =>
        string.IsNullOrWhiteSpace(SelectedEnvironmentLanguage)
            ? "Choose an input syntax to load its command libraries."
            : $"{SelectedEnvironmentLanguage} input syntax · only matching " +
              "libraries are available in the next stage.";

    public bool CanEditSelectedLanguageTab =>
        SelectedLibraryLanguageTab is
        {
            IsProfileLanguage: true,
            Libraries.Count: > 0
        };

    public string SelectedLanguageTabStatus =>
        SelectedLibraryLanguageTab?.StatusLabel
        ?? "Choose a language container.";

    internal static string NormalizeLanguageName(string? language)
    {
        return CommandLanguageRegistry.NormalizeDisplayName(language);
    }

    internal static bool LanguagesMatch(string? left, string? right) =>
        string.Equals(
            NormalizeLanguageName(left),
            NormalizeLanguageName(right),
            StringComparison.OrdinalIgnoreCase);

    private void RefreshLibraryLanguageTabs()
    {
        var profileLanguage = SelectedInputSyntax;
        LibraryLanguageTabs.Clear();

        var languages = CommandLanguageRegistry.OrderLanguages(
            LibraryLanguageOrder.Concat(LibrarySelections.Select(selection =>
                selection.Library.Language)));
        foreach (var language in languages)
        {
            var selections = LibrarySelections
                .Where(selection => LanguagesMatch(
                    selection.Library.Language,
                    language))
                .ToList();
            var tab = new LibraryLanguageTabViewModel(
                language,
                CreateLibraryGroups(language, selections));
            tab.SetProfileLanguage(profileLanguage);
            LibraryLanguageTabs.Add(tab);
        }

        SelectedLibraryLanguageTab = LibraryLanguageTabs.FirstOrDefault(tab =>
            LanguagesMatch(tab.Language, profileLanguage))
            ?? LibraryLanguageTabs.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedEnvironmentLanguage));
        OnPropertyChanged(nameof(ProfileLanguageRule));
        OnPropertyChanged(nameof(CanEditSelectedLanguageTab));
        OnPropertyChanged(nameof(SelectedLanguageTabStatus));
    }

    private static int GetLibraryKindOrder(CommandLibraryKind kind) =>
        kind switch
        {
            CommandLibraryKind.Framework => 0,
            CommandLibraryKind.Sdk => 1,
            CommandLibraryKind.CommandLineTool => 2,
            CommandLibraryKind.Core => 3,
            CommandLibraryKind.Language => 4,
            CommandLibraryKind.StandardLibrary => 5,
            CommandLibraryKind.Specification => 6,
            CommandLibraryKind.PlatformApi => 7,
            CommandLibraryKind.Runtime => 8,
            CommandLibraryKind.Module => 9,
            CommandLibraryKind.Package => 10,
            CommandLibraryKind.Extension => 11,
            _ => 12
        };

    private static IReadOnlyList<LibraryGroupViewModel>
        CreateLibraryGroups(
            string language,
            IReadOnlyCollection<LibrarySelectionViewModel> selections)
    {
        var groups = new List<LibraryGroupViewModel>();
        var assignedIds = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var roots = selections
            .Where(selection =>
                string.IsNullOrWhiteSpace(
                    selection.Library.ParentLibraryId)
                && selection.Library.Kind is
                    CommandLibraryKind.Framework
                    or CommandLibraryKind.Runtime
                    or CommandLibraryKind.Sdk
                    or CommandLibraryKind.CommandLineTool)
            .OrderBy(selection => selection.Library.CatalogPriority)
            .ThenBy(selection => GetLibraryKindOrder(
                selection.Library.Kind))
            .ThenBy(
                selection => selection.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var root in roots)
        {
            var children = selections
                .Where(selection => string.Equals(
                    selection.Library.ParentLibraryId,
                    root.Library.Id,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(selection => selection.Library.CatalogPriority)
                .ThenBy(selection => GetLibraryKindOrder(
                    selection.Library.Kind))
                .ThenBy(
                    selection => selection.DisplayName,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
            var items = new[] { root }
                .Concat(children)
                .ToList();
            foreach (var item in items)
            {
                assignedIds.Add(item.Library.Id);
            }

            var target = string.IsNullOrWhiteSpace(
                root.Library.TargetFramework)
                ? root.Library.Kind switch
                {
                    CommandLibraryKind.Runtime =>
                        $"{language} runtime",
                    CommandLibraryKind.Sdk =>
                        $"Installed SDK {root.Library.FrameworkVersion}",
                    CommandLibraryKind.CommandLineTool =>
                        $"Installed CLI {root.Library.FrameworkVersion}",
                    _ => $"{language} framework"
                }
                : root.Library.TargetFramework;
            groups.Add(new LibraryGroupViewModel(
                root.Library.Id,
                root.DisplayName,
                children.Count == 0
                    ? target
                    : $"{target} · {children.Count} attached " +
                      $"extension{(children.Count == 1 ? string.Empty : "s")}",
                items));
        }

        var languageSurfaces = selections
            .Where(selection =>
                selection.Library.Kind is
                    CommandLibraryKind.Language
                    or CommandLibraryKind.Core
                    or CommandLibraryKind.StandardLibrary
                    or CommandLibraryKind.Specification
                    or CommandLibraryKind.PlatformApi
                && !assignedIds.Contains(selection.Library.Id))
            .OrderBy(
                selection => selection.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (languageSurfaces.Count > 0)
        {
            foreach (var item in languageSurfaces)
            {
                assignedIds.Add(item.Library.Id);
            }

            groups.Add(new LibraryGroupViewModel(
                $"{NormalizeLanguageName(language)}-language",
                $"{NormalizeLanguageName(language)} language",
                "Language syntax and patterns that are not owned by a " +
                "framework namespace.",
                languageSurfaces));
        }

        var remaining = selections
            .Where(selection => !assignedIds.Contains(
                selection.Library.Id))
            .OrderBy(selection => selection.Library.CatalogPriority)
            .ThenBy(selection => GetLibraryKindOrder(
                selection.Library.Kind))
            .ThenBy(
                selection => selection.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (remaining.Count > 0)
        {
            groups.Add(new LibraryGroupViewModel(
                $"{NormalizeLanguageName(language)}-other",
                $"Other {NormalizeLanguageName(language)} libraries",
                "Libraries whose declared parent framework is not currently " +
                "available.",
                remaining));
        }

        return groups;
    }

    private bool EnsureProfileLanguageInvariant()
    {
        var changed = ApplyLibraryIdMigrations();
        var librariesById = Libraries.ToDictionary(
            library => library.Id,
            StringComparer.OrdinalIgnoreCase);
        var profiles = Environments.ToList();
        var splitProfiles = new List<EnvironmentProfile>();

        foreach (var profile in profiles)
        {
            profile.LibraryIds ??= [];
            var normalizedIds = profile.LibraryIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!profile.LibraryIds.SequenceEqual(
                    normalizedIds,
                    StringComparer.OrdinalIgnoreCase))
            {
                profile.LibraryIds = normalizedIds;
                changed = true;
            }

            var primaryLanguage = ResolveProfileLanguage(
                profile,
                librariesById);
            if (!LanguagesMatch(profile.Language, primaryLanguage)
                || !string.Equals(
                    profile.Language,
                    primaryLanguage,
                    StringComparison.Ordinal))
            {
                profile.Language = primaryLanguage;
                changed = true;
            }

            var incompatibleGroups = profile.LibraryIds
                .Where(librariesById.ContainsKey)
                .Select(id => new
                {
                    Id = id,
                    Language = NormalizeLanguageName(
                        librariesById[id].Language)
                })
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.Language)
                    && !LanguagesMatch(
                        item.Language,
                        primaryLanguage))
                .GroupBy(
                    item => item.Language,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var group in incompatibleGroups)
            {
                var splitIds = group
                    .Select(item => item.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                profile.LibraryIds.RemoveAll(id => splitIds.Contains(
                    id,
                    StringComparer.OrdinalIgnoreCase));
                changed = true;

                if (HasEquivalentLanguageProfile(
                        group.Key,
                        splitIds,
                        profile,
                        splitProfiles))
                {
                    continue;
                }

                splitProfiles.Add(new EnvironmentProfile
                {
                    Id = $"custom-{Guid.NewGuid():N}",
                    Name = CreateUniqueSplitProfileName(
                        profile.Name,
                        group.Key,
                        splitProfiles),
                    Description =
                        $"Recovered {group.Key} libraries from the former " +
                        $"mixed-language profile \"{profile.Name}\".",
                    Language = group.Key,
                    LibraryIds = splitIds
                });
            }

            if (!string.IsNullOrWhiteSpace(profile.SourceLibraryId)
                && librariesById.TryGetValue(
                    profile.SourceLibraryId,
                    out var sourceLibrary)
                && !LanguagesMatch(
                    sourceLibrary.Language,
                    profile.Language))
            {
                profile.SourceLibraryId = string.Empty;
                changed = true;
            }
        }

        foreach (var splitProfile in splitProfiles)
        {
            Environments.Add(splitProfile);
        }

        return changed;
    }

    private bool ApplyLibraryIdMigrations()
    {
        var aliases = Libraries
            .SelectMany(library => library.LegacyIds.Select(legacyId => new
            {
                LegacyId = legacyId,
                CurrentId = library.Id
            }))
            .GroupBy(item => item.LegacyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().CurrentId,
                StringComparer.OrdinalIgnoreCase);
        if (aliases.Count == 0)
        {
            return false;
        }

        var changed = false;
        foreach (var profile in Environments)
        {
            profile.LibraryIds ??= [];
            var migrated = profile.LibraryIds
                .Select(id => aliases.TryGetValue(id, out var currentId)
                    ? currentId
                    : id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!profile.LibraryIds.SequenceEqual(
                    migrated,
                    StringComparer.OrdinalIgnoreCase))
            {
                profile.LibraryIds = migrated;
                changed = true;
            }

            if (aliases.TryGetValue(
                    profile.SourceLibraryId,
                    out var migratedSourceId))
            {
                profile.SourceLibraryId = migratedSourceId;
                changed = true;
            }
        }

        return changed;
    }

    private string ResolveProfileLanguage(
        EnvironmentProfile profile,
        IReadOnlyDictionary<string, CommandLibrary> librariesById)
    {
        if (!string.IsNullOrWhiteSpace(profile.SourceLibraryId)
            && librariesById.TryGetValue(
                profile.SourceLibraryId,
                out var sourceLibrary))
        {
            var sourceLanguage =
                NormalizeLanguageName(sourceLibrary.Language);
            if (!string.IsNullOrWhiteSpace(sourceLanguage))
            {
                return sourceLanguage;
            }
        }

        var configuredLanguage = NormalizeLanguageName(profile.Language);
        var catalogLanguages = librariesById.Values
            .Select(library => NormalizeLanguageName(library.Language))
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(configuredLanguage)
            && (LibraryLanguageOrder.Contains(
                    configuredLanguage,
                    StringComparer.OrdinalIgnoreCase)
                || catalogLanguages.Contains(configuredLanguage)))
        {
            return configuredLanguage;
        }

        var profileIdentity = $"{profile.Id} {profile.Name}";
        if (profileIdentity.Contains(
                "csharp",
                StringComparison.OrdinalIgnoreCase)
            || profileIdentity.Contains(
                "c#",
                StringComparison.OrdinalIgnoreCase)
            || profileIdentity.Contains(
                ".net",
                StringComparison.OrdinalIgnoreCase))
        {
            return "C#";
        }

        if (profileIdentity.Contains(
                "powershell",
                StringComparison.OrdinalIgnoreCase)
            || profileIdentity.Contains(
                "pwsh",
                StringComparison.OrdinalIgnoreCase))
        {
            return "PowerShell";
        }

        var mostCommonLanguage = profile.LibraryIds
            .Where(librariesById.ContainsKey)
            .Select(id => NormalizeLanguageName(
                librariesById[id].Language))
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .GroupBy(
                language => language,
                StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group =>
            {
                var knownIndex = Array.FindIndex(
                    LibraryLanguageOrder,
                    language => LanguagesMatch(language, group.Key));
                return knownIndex < 0
                    ? int.MaxValue
                    : knownIndex;
            })
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .FirstOrDefault();
        return mostCommonLanguage ?? "PowerShell";
    }

    private bool HasEquivalentLanguageProfile(
        string language,
        IReadOnlyCollection<string> libraryIds,
        EnvironmentProfile sourceProfile,
        IReadOnlyCollection<EnvironmentProfile> pendingProfiles)
    {
        return Environments
            .Concat(pendingProfiles)
            .Where(profile => !ReferenceEquals(profile, sourceProfile))
            .Any(profile =>
                LanguagesMatch(profile.Language, language)
                && LibraryIdSetsEqual(
                    profile.LibraryIds,
                    libraryIds));
    }

    private static bool LibraryIdSetsEqual(
        IEnumerable<string> left,
        IEnumerable<string> right)
    {
        var leftSet = left.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rightSet = right.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return leftSet.SetEquals(rightSet);
    }

    private string CreateUniqueSplitProfileName(
        string sourceName,
        string language,
        IReadOnlyCollection<EnvironmentProfile> pendingProfiles)
    {
        var baseName = $"{sourceName} — {language}";
        var candidate = baseName;
        var suffix = 2;
        while (Environments
                   .Concat(pendingProfiles)
                   .Any(profile => string.Equals(
                       profile.Name,
                       candidate,
                       StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} {suffix++}";
        }

        return candidate;
    }
}

public sealed class LibraryLanguageTabViewModel : ObservableObject
{
    private bool _isProfileLanguage;
    private string _profileLanguage = string.Empty;

    public LibraryLanguageTabViewModel(
        string language,
        IEnumerable<LibraryGroupViewModel> groups)
    {
        Language = CommandBuilderViewModel.NormalizeLanguageName(language);
        Groups = new ObservableCollection<LibraryGroupViewModel>(groups);
        Libraries = new ObservableCollection<LibrarySelectionViewModel>(
            Groups.SelectMany(group => group.Libraries));
    }

    public string Language { get; }
    public string DisplayName => Language;
    public ObservableCollection<LibraryGroupViewModel> Groups { get; }
    public ObservableCollection<LibrarySelectionViewModel> Libraries { get; }
    public string CountLabel =>
        $"{Libraries.Count} item{(Libraries.Count == 1 ? string.Empty : "s")}";

    public bool IsProfileLanguage
    {
        get => _isProfileLanguage;
        private set => SetProperty(ref _isProfileLanguage, value);
    }

    public string StatusLabel => IsProfileLanguage
        ? $"{Language} library items available to the active input syntax. Frameworks, " +
          "SDKs, command-line tools, and language surfaces are kept ahead of " +
          "optional content modules."
        : $"Viewing {Language}. Switch Input Syntax from {_profileLanguage} " +
          $"to {Language} to activate these libraries.";

    public void SetProfileLanguage(string profileLanguage)
    {
        _profileLanguage =
            CommandBuilderViewModel.NormalizeLanguageName(profileLanguage);
        IsProfileLanguage = CommandBuilderViewModel.LanguagesMatch(
            Language,
            _profileLanguage);
        OnPropertyChanged(nameof(StatusLabel));
    }
}

public sealed class LibraryGroupViewModel
{
    public LibraryGroupViewModel(
        string id,
        string displayName,
        string description,
        IEnumerable<LibrarySelectionViewModel> libraries)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Libraries = new ObservableCollection<LibrarySelectionViewModel>(
            libraries);
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public ObservableCollection<LibrarySelectionViewModel> Libraries { get; }
    public string CountLabel =>
        $"{Libraries.Count} item{(Libraries.Count == 1 ? string.Empty : "s")}";
}
