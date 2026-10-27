using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Services;
using System.Collections.ObjectModel;

namespace Hypershell.ViewModels;

public sealed partial class CommandBuilderViewModel
{
    private static readonly IReadOnlyList<DotNetInvocationSyntaxOption>
        DotNetInvocationSyntaxChoices =
        [
            new(
                ExecutionTarget.CSharp,
                "C#",
                "Generate new Type(), instance.Member(), and Type.StaticMember() statements.",
                "var client = new System.Net.Http.HttpClient();"),
            new(
                ExecutionTarget.PowerShell,
                "PowerShell",
                "Generate [Type]::new(), $instance.Member(), and [Type]::StaticMember() statements.",
                "$client = [System.Net.Http.HttpClient]::new()")
        ];

    private readonly DotNetApiIndexService _dotNetApiIndexService = new();
    private readonly Dictionary<string, DotNetApiIndex> _dotNetApiIndexes =
        new(StringComparer.OrdinalIgnoreCase);
    private BuilderSurfaceOption? _selectedBuilderSurface;
    private string _selectedNamespacePath = string.Empty;
    private CSharpTypeOptionViewModel? _selectedCSharpType;
    private CSharpMemberOptionViewModel? _selectedCSharpMember;
    private CSharpOverloadOptionViewModel? _selectedCSharpOverload;
    private CommandDefinition? _selectedCSharpLanguagePattern;
    private DotNetInvocationSyntaxOption _selectedDotNetInvocationSyntax =
        DotNetInvocationSyntaxChoices[0];
    private bool _isRefreshingBuilderSurfaces;

    public ObservableCollection<BuilderSurfaceOption> BuilderSurfaces { get; } =
        [];
    public ObservableCollection<CSharpNamespaceLevelViewModel>
        CSharpNamespaceLevels { get; } = [];
    public ObservableCollection<CSharpTypeOptionViewModel> CSharpTypes { get; } =
        [];
    public ObservableCollection<CSharpMemberOptionViewModel> CSharpMembers
        { get; } = [];
    public ObservableCollection<CSharpOverloadOptionViewModel> CSharpOverloads
        { get; } = [];
    public ObservableCollection<CommandDefinition> CSharpLanguagePatterns
        { get; } = [];
    public IReadOnlyList<DotNetInvocationSyntaxOption>
        DotNetInvocationSyntaxOptions => DotNetInvocationSyntaxChoices;

    public DotNetInvocationSyntaxOption SelectedDotNetInvocationSyntax
    {
        get => _selectedDotNetInvocationSyntax;
        set
        {
            if (value is null
                || !SetProperty(
                    ref _selectedDotNetInvocationSyntax,
                    value))
            {
                return;
            }

            RefreshSelectedRuntimeCommand();
            OnPropertyChanged(nameof(DotNetInvocationSyntaxHint));
            OnPropertyChanged(nameof(DotNetApiSurfaceLabel));
            OnPropertyChanged(nameof(CSharpOverloadHint));
            OnPropertyChanged(nameof(CommandGuideContextLabel));
            if (IsCSharpComposerWorkspace)
            {
                NotifyCSharpComposerChanged();
                StatusMessage = string.Empty;
            }
            else
            {
                RefreshStagedStatement();
            }
        }
    }

    public bool SupportsDotNetInvocationSyntax =>
        IsIndexedDotNetSurfaceSelected;
    public string DotNetInvocationSyntaxHint =>
        $"{SelectedDotNetInvocationSyntax.Description} " +
        "The selected syntax determines which terminal receives the statement.";

    public BuilderSurfaceOption? SelectedBuilderSurface
    {
        get => _selectedBuilderSurface;
        set
        {
            if (!SetProperty(ref _selectedBuilderSurface, value))
            {
                return;
            }

            if (_isRefreshingBuilderSurfaces)
            {
                return;
            }

            ResetBuilderSelection();
            if (IsCSharpLanguageSurface)
            {
                RefreshCSharpLanguagePatterns();
            }
            else if (IsHierarchySurface)
            {
                RefreshHierarchyBrowser();
            }
            else if (IsCSharpSurface)
            {
                RefreshCSharpNamespaceRoot();
            }
            else
            {
                RefreshCommandFacets();
            }

            NotifyBuilderSurfaceChanged();
            if (value is { IncludesRuntimeIndex: true }
                && SelectedDotNetApiIndex is null)
            {
                _ = LoadSelectedManagedApiIndexAsync(value);
            }

            _ = MaterializeBuilderSurfaceAsync(value);
        }
    }

    public bool IsCSharpSurface =>
        SelectedBuilderSurface?.ExecutionTarget == ExecutionTarget.CSharp;
    public bool IsCSharpLanguageSurface =>
        IsCSharpSurface
        && SelectedBuilderSurface?.Kind is
            CommandLibraryKind.Language or CommandLibraryKind.Core;
    public bool IsCSharpApiSurface =>
        IsCSharpSurface && !IsCSharpLanguageSurface;
    public bool IsDotNetToolchainSurface =>
        DotNetToolchainCatalogService.IsToolchainLibraryId(
            SelectedBuilderSurface?.LibraryId);
    public bool IsCommandFacetSurface =>
        SelectedBuilderSurface is not null
        && !IsCSharpSurface
        && !IsCSharpComposerWorkspace
        && !IsHierarchySurface;
    public bool HasMultipleBuilderSurfaces => BuilderSurfaces.Count > 1;
    public string BuilderSurfaceDescription =>
        SelectedBuilderSurface?.Description
        ?? $"Select a {SelectedInputSyntax} library, framework, module, or tool.";
    public string ActiveBuilderLanguageSummary =>
        $"{SelectedInputSyntax} · " +
        $"{BuilderSurfaces.Count(surface => !string.IsNullOrWhiteSpace(surface.LibraryId))} " +
        $"available surface{(BuilderSurfaces.Count(surface => !string.IsNullOrWhiteSpace(surface.LibraryId)) == 1 ? string.Empty : "s")}";
    public string ActiveBuilderProfileSummary =>
        ActiveBuilderLanguageSummary;

    private CommandLibrary? SelectedSurfaceLibrary =>
        Libraries.FirstOrDefault(library => string.Equals(
            library.Id,
            SelectedBuilderSurface?.LibraryId,
            StringComparison.OrdinalIgnoreCase));

    private DotNetApiIndex? SelectedDotNetApiIndex =>
        SelectedBuilderSurface is { IncludesRuntimeIndex: true } surface
        && _dotNetApiIndexes.TryGetValue(surface.LibraryId, out var index)
            ? index
            : null;

    private bool IsIndexedDotNetSurfaceSelected =>
        SelectedBuilderSurface?.IncludesRuntimeIndex == true;

    private IEnumerable<DotNetApiIndex> GetSelectedDotNetApiIndexes()
    {
        if (IsCSharpComposerWorkspace)
        {
            if (SelectedDotNetApiIndex is { } selectedIndex)
            {
                yield return selectedIndex;
            }

            yield break;
        }

        foreach (var selection in LibrarySelections.Where(selection =>
                     selection.IsSelected))
        {
            if (_dotNetApiIndexes.TryGetValue(
                    selection.Library.Id,
                    out var index))
            {
                yield return index;
            }
        }
    }

    private bool HasSelectedDotNetFrameworkIndex =>
        GetSelectedDotNetApiIndexes().Any();

    private CommandLibrary? SelectedUnsupportedCSharpFramework =>
        SelectedSurfaceLibrary is
        {
            ExecutionTarget: ExecutionTarget.CSharp,
            Kind: CommandLibraryKind.Framework,
            IsExecutionSupported: false
        } library
            ? library
            : null;

    public string DotNetApiSurfaceLabel
    {
        get
        {
            var surfaceLibrary = SelectedSurfaceLibrary;
            if (surfaceLibrary is null)
            {
                return "Choose a C# library surface from the active profile.";
            }

            if (surfaceLibrary.Kind == CommandLibraryKind.Language)
            {
                return $"{surfaceLibrary.DisplayName} · " +
                       $"{surfaceLibrary.Commands.Count:N0} language patterns";
            }

            if (!IsIndexedDotNetSurfaceSelected)
            {
                var framework = SelectedUnsupportedCSharpFramework;
                if (framework is not null)
                {
                    return $"{framework.DisplayName} reference pack selected · " +
                           "Command Builder adapter pending";
                }

                return $"{surfaceLibrary.DisplayName} · " +
                       $"{surfaceLibrary.Commands.Count:N0} authored commands · " +
                       $"{surfaceLibrary.TargetFramework}";
            }

            var index = SelectedDotNetApiIndex;
            return index is null
                ? $"Loading the installed {surfaceLibrary.DisplayName} API surface…"
                : $"{index.FrameworkDescription} · " +
                  $"{index.NamespaceCount:N0} namespaces · " +
                  $"{index.Types.Count:N0} public types · " +
                  $"{index.OverloadCount:N0} callable variations · " +
                  $"generate {SelectedDotNetInvocationSyntax.DisplayName}";
        }
    }

    public string SelectedNamespacePath
    {
        get => _selectedNamespacePath;
        private set
        {
            if (SetProperty(ref _selectedNamespacePath, value))
            {
                OnPropertyChanged(nameof(NamespaceBreadcrumb));
                OnPropertyChanged(nameof(NamespaceHint));
                OnPropertyChanged(nameof(HasSelectedNamespace));
            }
        }
    }

    public string NamespaceBreadcrumb =>
        string.IsNullOrWhiteSpace(SelectedNamespacePath)
            ? "Choose a root namespace"
            : SelectedNamespacePath.Replace(".", "  ›  ");
    public bool HasSelectedNamespace =>
        !string.IsNullOrWhiteSpace(SelectedNamespacePath);
    public string NamespaceHint
    {
        get
        {
            if (IsCSharpLanguageSurface)
            {
                return "Choose a language pattern instead of a framework namespace.";
            }

            var surfaceLibrary = SelectedSurfaceLibrary;
            if (surfaceLibrary is null)
            {
                return "Choose a library surface from the active profile.";
            }

            if (!IsIndexedDotNetSurfaceSelected
                && SelectedUnsupportedCSharpFramework is { } framework)
            {
                return $"{framework.DisplayName} is organized as a separate " +
                       "framework surface; its namespace adapter is not active yet.";
            }

            if (IsIndexedDotNetSurfaceSelected
                && SelectedDotNetApiIndex is null)
            {
                return $"Reading public types and members from the active " +
                       $"{surfaceLibrary.DisplayName} reference pack.";
            }

            if (!HasSelectedNamespace)
            {
                return "Choose a root. Additional namespace segment boxes appear only when that path has children.";
            }

            var directTypes = GetCurrentDirectTypeCount(
                SelectedNamespacePath);
            return directTypes == 0
                ? "This namespace path is a branch. Continue into a child namespace to expose its types."
                : $"{directTypes:N0} available type{(directTypes == 1 ? string.Empty : "s")} declared directly in {SelectedNamespacePath}.";
        }
    }

    public CSharpTypeOptionViewModel? SelectedCSharpType
    {
        get => _selectedCSharpType;
        set
        {
            if (!SetProperty(ref _selectedCSharpType, value))
            {
                return;
            }

            SelectedCSharpMember = null;
            RefreshCSharpMembers();
            OnPropertyChanged(nameof(HasSelectedCSharpType));
            OnPropertyChanged(nameof(CSharpTypeHint));
            OnPropertyChanged(nameof(CommandGuideContextLabel));
        }
    }

    public bool HasSelectedCSharpType => SelectedCSharpType is not null;
    public string CSharpTypeHint => !HasSelectedNamespace
        ? "Complete a namespace path to reveal its directly declared public types."
        : CSharpTypes.Count == 0
            ? "No public types are declared directly at this namespace level."
            : "Choose a type. Its card identifies static classes and whether " +
              "an object can be constructed.";

    public CSharpMemberOptionViewModel? SelectedCSharpMember
    {
        get => _selectedCSharpMember;
        set
        {
            if (!SetProperty(ref _selectedCSharpMember, value))
            {
                return;
            }

            SelectedCSharpOverload = null;
            RefreshCSharpOverloads();
            OnPropertyChanged(nameof(HasSelectedCSharpMember));
            OnPropertyChanged(nameof(CSharpMemberHint));
            OnPropertyChanged(nameof(CommandGuideContextLabel));
        }
    }

    public bool HasSelectedCSharpMember => SelectedCSharpMember is not null;
    public string CSharpMemberHint => !HasSelectedCSharpType
        ? "Choose a type to reveal its declared public members."
        : CSharpMembers.Count == 0
            ? "No supported public members were found for this type."
            : "Choose new, a method, or a property. Each entry is labelled " +
              "static, instance, or creates instance.";

    public CSharpOverloadOptionViewModel? SelectedCSharpOverload
    {
        get => _selectedCSharpOverload;
        set
        {
            if (!SetProperty(ref _selectedCSharpOverload, value))
            {
                return;
            }

            if (value?.AuthoredCommand is not null)
            {
                SelectedCommand = value.AuthoredCommand;
            }
            else if (value?.RuntimeOverload is not null
                     && SelectedDotNetApiIndex is not null
                     && SelectedCSharpType?.RuntimeType is not null
                     && SelectedCSharpMember?.RuntimeMember is not null)
            {
                RefreshSelectedRuntimeCommand();
            }
            else
            {
                SelectedCommand = null;
            }

            OnPropertyChanged(nameof(HasSelectedCSharpOverload));
            OnPropertyChanged(nameof(CSharpOverloadHint));
            OnPropertyChanged(nameof(CommandGuideContextLabel));
        }
    }

    public bool HasSelectedCSharpOverload => SelectedCSharpOverload is not null;
    public string CSharpOverloadHint => !HasSelectedCSharpMember
        ? "Choose a member to reveal each available signature."
        : CSharpOverloads.Count == 0
            ? "No usable signatures are available for this member."
            : IsIndexedDotNetSurfaceSelected
                ? "Choose the exact signature; the statement will be generated " +
                  $"for {SelectedDotNetInvocationSyntax.DisplayName}."
                : "Choose an authored signature from the selected profile library.";

    private void RefreshSelectedRuntimeCommand()
    {
        if (SelectedCSharpOverload?.AuthoredCommand is { } authored)
        {
            SelectedCommand = authored;
            return;
        }

        if (SelectedCSharpOverload?.RuntimeOverload is not { } overload
            || SelectedDotNetApiIndex is not { } index
            || SelectedCSharpType?.RuntimeType is not { } type
            || SelectedCSharpMember?.RuntimeMember is not { } member)
        {
            SelectedCommand = null;
            return;
        }

        SelectedCommand = index.CreateCommand(
            type,
            member,
            overload,
            SelectedDotNetInvocationSyntax.ExecutionTarget);
    }

    public CommandDefinition? SelectedCSharpLanguagePattern
    {
        get => _selectedCSharpLanguagePattern;
        set
        {
            if (!SetProperty(ref _selectedCSharpLanguagePattern, value))
            {
                return;
            }

            SelectedCommand = value;
            OnPropertyChanged(nameof(HasSelectedCSharpLanguagePattern));
            OnPropertyChanged(nameof(CSharpLanguagePatternHint));
        }
    }

    public bool HasSelectedCSharpLanguagePattern =>
        SelectedCSharpLanguagePattern is not null;
    public string CSharpLanguagePatternHint =>
        CSharpLanguagePatterns.Count == 0
            ? "The selected language library does not contain any patterns."
            : HasSelectedCSharpLanguagePattern
                ? SelectedCSharpLanguagePattern?.Description ?? string.Empty
                : "Choose a syntax pattern supplied by this profile library.";

    public bool IsCSharpLanguagePattern =>
        IsCSharpSurface
        && SelectedCommand?.ExecutionTarget == ExecutionTarget.CSharp
        && string.IsNullOrWhiteSpace(SelectedCommand.NamespaceName);

    private void ApplyDotNetApiIndexes(
        IEnumerable<DotNetApiIndex> indexes)
    {
        _dotNetApiIndexes.Clear();
        foreach (var index in indexes)
        {
            _dotNetApiIndexes[index.LibraryId] = index;
        }

        OnPropertyChanged(nameof(DotNetApiSurfaceLabel));
        OnPropertyChanged(nameof(SupportsDotNetInvocationSyntax));
        if (IsCSharpApiSurface)
        {
            RefreshCSharpNamespaceRoot();
        }

        OnCSharpComposerSurfaceChanged();
        RefreshSearchResults();
    }

    private async Task LoadSelectedManagedApiIndexAsync(
        BuilderSurfaceOption surface)
    {
        var library = Libraries.FirstOrDefault(candidate => string.Equals(
            candidate.Id,
            surface.LibraryId,
            StringComparison.OrdinalIgnoreCase));
        if (library is null
            || !CSharpFrameworkCatalogService.IsIndexedManagedApiLibrary(
                library))
        {
            return;
        }

        try
        {
            var index = await _dotNetApiIndexService.LoadAsync(library);
            _dotNetApiIndexes[index.LibraryId] = index;
            if (!string.Equals(
                    SelectedBuilderSurface?.LibraryId,
                    surface.LibraryId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            OnPropertyChanged(nameof(DotNetApiSurfaceLabel));
            OnPropertyChanged(nameof(SupportsDotNetInvocationSyntax));
            RefreshCSharpNamespaceRoot();
            OnCSharpComposerSurfaceChanged();
            RefreshSearchResults();
        }
        catch (Exception exception)
        {
            if (string.Equals(
                    SelectedBuilderSurface?.LibraryId,
                    surface.LibraryId,
                    StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage =
                    $"Unable to read {library.DisplayName}: {exception.Message}";
            }
        }
    }

    private void RefreshBuilderSurfaces()
    {
        var previousLibraryId = SelectedBuilderSurface?.LibraryId;
        var isCSharpLanguageWorkspace = LanguagesMatch(
            SelectedEnvironmentLanguage,
            "C#");
        var activeLibraries = (isCSharpLanguageWorkspace
                ? Libraries.Where(library =>
                    LanguagesMatch(library.Language, "C#")
                    && library.Kind is
                        CommandLibraryKind.Core
                        or CommandLibraryKind.Framework
                        or CommandLibraryKind.Sdk
                        or CommandLibraryKind.CommandLineTool
                        or CommandLibraryKind.Package
                        or CommandLibraryKind.Module
                        or CommandLibraryKind.Extension)
                : Libraries.Where(library => LanguagesMatch(
                    library.Language,
                    SelectedInputSyntax)))
            .OrderBy(library => isCSharpLanguageWorkspace
                ? library.CatalogPriority
                : library.ExecutionTarget switch
                {
                    ExecutionTarget.PowerShell => 0,
                    ExecutionTarget.CSharp => 1,
                    _ => 2
                })
            .ThenBy(library => isCSharpLanguageWorkspace
                ? 0
                : GetLibraryKindOrder(library.Kind))
            .ThenBy(
                library => library.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
        var options = activeLibraries
            .Select(CreateSurfaceOption)
            .ToList();
        if (isCSharpLanguageWorkspace)
        {
            options.Insert(
                0,
                new BuilderSurfaceOption(
                    string.Empty,
                    ExecutionTarget.External,
                    "Select C# Surface",
                    "Choose C# Core, a Base Class Library version, SDK, command-line tool, or package recipe.",
                    "C#",
                    CommandLibraryKind.Content,
                    false));
        }

        _isRefreshingBuilderSurfaces = true;
        try
        {
            ReplaceItems(BuilderSurfaces, options);
            var preferredLibraryId = previousLibraryId;
            if (string.IsNullOrWhiteSpace(preferredLibraryId)
                || !activeLibraries.Any(library => string.Equals(
                    library.Id,
                    preferredLibraryId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                preferredLibraryId = isCSharpLanguageWorkspace
                    ? string.Empty
                    : activeLibraries.FirstOrDefault()?.Id;
            }

            _selectedBuilderSurface = options.FirstOrDefault(option =>
                string.Equals(
                    option.LibraryId,
                    preferredLibraryId,
                    StringComparison.OrdinalIgnoreCase));
            OnPropertyChanged(nameof(SelectedBuilderSurface));
        }
        finally
        {
            _isRefreshingBuilderSurfaces = false;
        }

        ResetBuilderSelection();
        if (IsCSharpLanguageSurface)
        {
            RefreshCSharpLanguagePatterns();
        }
        else if (IsHierarchySurface)
        {
            RefreshHierarchyBrowser();
        }
        else if (IsCSharpSurface)
        {
            RefreshCSharpNamespaceRoot();
        }
        else
        {
            RefreshCommandFacets();
        }

        NotifyBuilderSurfaceChanged();
        _ = MaterializeBuilderSurfaceAsync(SelectedBuilderSurface);
    }

    private BuilderSurfaceOption CreateSurfaceOption(
        CommandLibrary library)
    {
        var parent = Libraries.FirstOrDefault(candidate => string.Equals(
            candidate.Id,
            library.ParentLibraryId,
            StringComparison.OrdinalIgnoreCase));
        var groupLabel = parent?.DisplayName
                         ?? (library.Kind is
                                 CommandLibraryKind.Language
                                 or CommandLibraryKind.Core
                              ? $"{library.Language} language"
                             : !string.IsNullOrWhiteSpace(
                                 library.FrameworkName)
                                 ? library.FrameworkName
                                 : library.Language);
        var description = library.ExecutionTarget switch
        {
            ExecutionTarget.CSharp
                when library.Kind is
                    CommandLibraryKind.Language or CommandLibraryKind.Core =>
                $"{library.CoverageSummary} · {library.ActiveVersionLabel}",
            ExecutionTarget.CSharp
                when CSharpFrameworkCatalogService
                    .IsIndexedManagedApiLibrary(library) =>
                $"Complete installed {library.DisplayName} namespace, type, member, and overload " +
                "index with C# or PowerShell generation.",
            ExecutionTarget.CSharp
                when library.Kind == CommandLibraryKind.Framework =>
                library.ExecutionSupportMessage,
            ExecutionTarget.CSharp =>
                $"{library.EffectiveEntryCount:N0} authored APIs targeting " +
                $"{library.TargetFramework}.",
            ExecutionTarget.PowerShell
                when library.Kind == CommandLibraryKind.Sdk =>
                $"{library.EffectiveEntryCount:N0} commands discovered from the " +
                $"installed .NET SDK {library.FrameworkVersion}.",
            ExecutionTarget.PowerShell
                when library.Kind == CommandLibraryKind.CommandLineTool =>
                $"{library.EffectiveEntryCount:N0} driver operations exposed by " +
                $"the installed .NET CLI {library.FrameworkVersion}.",
            ExecutionTarget.PowerShell =>
                $"{library.EffectiveEntryCount:N0} commands using PowerShell's native Verb-Noun navigation.",
            _ =>
                library.CoverageSummary + " · " +
                library.ActiveVersionLabel
        };
        return new BuilderSurfaceOption(
            library.Id,
            library.ExecutionTarget,
            library.DisplayName,
            description,
            groupLabel,
            library.Kind,
            CSharpFrameworkCatalogService.IsIndexedManagedApiLibrary(
                library));
    }

    private void NotifyBuilderSurfaceChanged()
    {
        OnPropertyChanged(nameof(IsCSharpSurface));
        OnPropertyChanged(nameof(IsCSharpLanguageSurface));
        OnPropertyChanged(nameof(IsCSharpApiSurface));
        OnPropertyChanged(nameof(IsDotNetToolchainSurface));
        OnPropertyChanged(nameof(IsCommandFacetSurface));
        OnPropertyChanged(nameof(IsHierarchySurface));
        OnPropertyChanged(nameof(PrimaryFacetHeading));
        OnPropertyChanged(nameof(SecondaryFacetHeading));
        OnPropertyChanged(nameof(PrimaryFacetToolTip));
        OnPropertyChanged(nameof(SecondaryFacetToolTip));
        OnPropertyChanged(nameof(ActionHint));
        OnPropertyChanged(nameof(ObjectHint));
        OnPropertyChanged(nameof(SelectedActionDescriptionHeading));
        OnPropertyChanged(nameof(SelectedObjectDescriptionHeading));
        OnPropertyChanged(nameof(SupportsDotNetInvocationSyntax));
        OnPropertyChanged(nameof(DotNetInvocationSyntaxHint));
        OnPropertyChanged(nameof(HasMultipleBuilderSurfaces));
        OnPropertyChanged(nameof(BuilderSurfaceDescription));
        OnPropertyChanged(nameof(DotNetApiSurfaceLabel));
        OnPropertyChanged(nameof(NamespaceHint));
        OnPropertyChanged(nameof(ParameterSectionHeading));
        OnPropertyChanged(nameof(ParameterSelectorHint));
        OnPropertyChanged(nameof(SelectedCommandMetadataStatus));
        OnPropertyChanged(nameof(SelectedCommandDescription));
        OnPropertyChanged(nameof(CommandGuideContextLabel));
        OnPropertyChanged(nameof(IsCSharpLanguagePattern));
        OnPropertyChanged(nameof(ActiveBuilderProfileSummary));
        OnPropertyChanged(nameof(IsCSharpRecipeSurface));
        OnPropertyChanged(nameof(IsCSharpComposerWorkspace));
        OnPropertyChanged(nameof(IsCatalogCommandWorkspace));
        OnPropertyChanged(nameof(IsProfileDrivenBuilderWorkspace));
        OnPropertyChanged(nameof(BuilderSurfaceHeading));
        OnPropertyChanged(nameof(BuilderSurfaceToolTip));
        OnPropertyChanged(nameof(SearchSurfaceHeading));
        OnPropertyChanged(nameof(ActiveBuilderLanguageSummary));
        OnCSharpComposerSurfaceChanged();
        RefreshSearchResults();
        RefreshStagedStatement();
    }

    private void ResetBuilderSelection()
    {
        _selectedAction = null;
        _selectedObject = null;
        OnPropertyChanged(nameof(SelectedAction));
        OnPropertyChanged(nameof(SelectedObject));
        OnPropertyChanged(nameof(HasSelectedAction));
        OnPropertyChanged(nameof(HasSelectedObject));
        Actions.Clear();
        Objects.Clear();
        MatchingCommands.Clear();

        ClearCSharpNavigation();
        CSharpLanguagePatterns.Clear();
        _selectedCSharpLanguagePattern = null;
        OnPropertyChanged(nameof(SelectedCSharpLanguagePattern));
        OnPropertyChanged(nameof(HasSelectedCSharpLanguagePattern));
        OnPropertyChanged(nameof(CSharpLanguagePatternHint));
        SelectedCommand = null;
    }

    private void ClearCSharpNavigation()
    {
        foreach (var level in CSharpNamespaceLevels)
        {
            level.SelectionChanged -= OnCSharpNamespaceLevelChanged;
        }

        CSharpNamespaceLevels.Clear();
        CSharpTypes.Clear();
        CSharpMembers.Clear();
        CSharpOverloads.Clear();
        _selectedCSharpType = null;
        _selectedCSharpMember = null;
        _selectedCSharpOverload = null;
        SelectedNamespacePath = string.Empty;
        OnPropertyChanged(nameof(SelectedCSharpType));
        OnPropertyChanged(nameof(SelectedCSharpMember));
        OnPropertyChanged(nameof(SelectedCSharpOverload));
        OnPropertyChanged(nameof(HasSelectedCSharpType));
        OnPropertyChanged(nameof(HasSelectedCSharpMember));
        OnPropertyChanged(nameof(HasSelectedCSharpOverload));
    }

    private void RefreshCSharpNamespaceRoot()
    {
        ClearCSharpNavigation();
        if (!IsCSharpApiSurface
            || (IsIndexedDotNetSurfaceSelected
                && SelectedDotNetApiIndex is null))
        {
            OnPropertyChanged(nameof(NamespaceHint));
            return;
        }

        AddNamespaceLevel(
            0,
            GetCurrentNamespaceChildren(null));
        OnPropertyChanged(nameof(NamespaceHint));
        OnPropertyChanged(nameof(CSharpTypeHint));
    }

    private void RefreshCSharpLanguagePatterns()
    {
        var previousName = SelectedCSharpLanguagePattern?.Name;
        var patterns = GetActiveCSharpCommands()
            .Where(command => string.IsNullOrWhiteSpace(
                command.NamespaceName))
            .OrderBy(
                command => command.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
        ReplaceItems(CSharpLanguagePatterns, patterns);
        SelectedCSharpLanguagePattern =
            CSharpLanguagePatterns.FirstOrDefault(command => string.Equals(
                command.Name,
                previousName,
                StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(CSharpLanguagePatternHint));
    }

    private void AddNamespaceLevel(
        int depth,
        IReadOnlyList<DotNetNamespaceDefinition> definitions,
        string? parentNamespace = null)
    {
        if (definitions.Count == 0)
        {
            return;
        }

        var options = definitions
            .Select(CSharpNamespaceOptionViewModel.FromDefinition)
            .ToList();
        if (depth > 0
            && !string.IsNullOrWhiteSpace(parentNamespace))
        {
            options.Insert(
                0,
                CSharpNamespaceOptionViewModel.CreateParentSelection(
                    parentNamespace));
        }

        var level = new CSharpNamespaceLevelViewModel(depth, options);
        level.SelectionChanged += OnCSharpNamespaceLevelChanged;
        CSharpNamespaceLevels.Add(level);
    }

    private void OnCSharpNamespaceLevelChanged(
        object? sender,
        EventArgs eventArgs)
    {
        if (sender is not CSharpNamespaceLevelViewModel level
            || !IsCSharpApiSurface)
        {
            return;
        }

        var levelIndex = CSharpNamespaceLevels.IndexOf(level);
        if (levelIndex < 0)
        {
            return;
        }

        while (CSharpNamespaceLevels.Count > levelIndex + 1)
        {
            var removed = CSharpNamespaceLevels[^1];
            removed.SelectionChanged -= OnCSharpNamespaceLevelChanged;
            CSharpNamespaceLevels.RemoveAt(CSharpNamespaceLevels.Count - 1);
        }

        var selectedOption = level.SelectedOption;
        SelectedNamespacePath = selectedOption?.FullName
                                ?? (levelIndex > 0
                                    ? CSharpNamespaceLevels[levelIndex - 1]
                                        .SelectedOption?.FullName
                                      ?? string.Empty
                                    : string.Empty);
        RefreshCSharpTypes();

        if (selectedOption is
            {
                IsParentNamespaceSelection: false
            })
        {
            AddNamespaceLevel(
                levelIndex + 1,
                GetCurrentNamespaceChildren(
                    selectedOption.FullName),
                selectedOption.FullName);
        }
    }

    private IReadOnlyList<DotNetNamespaceDefinition>
        GetCurrentNamespaceChildren(string? parentPath)
    {
        if (SelectedDotNetApiIndex is { } index)
        {
            return index.GetNamespaceChildren(parentPath);
        }

        var normalizedParent = parentPath?.Trim().Trim('.')
                               ?? string.Empty;
        var prefix = string.IsNullOrWhiteSpace(normalizedParent)
            ? string.Empty
            : $"{normalizedParent}.";
        var commands = GetActiveCSharpCommands()
            .Where(command => !string.IsNullOrWhiteSpace(
                command.NamespaceName))
            .ToList();
        var childSegments = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands)
        {
            if (!command.NamespaceName.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder = command.NamespaceName[prefix.Length..];
            var separator = remainder.IndexOf('.');
            var segment = separator >= 0
                ? remainder[..separator]
                : remainder;
            if (!string.IsNullOrWhiteSpace(segment))
            {
                childSegments.Add(segment);
            }
        }

        return childSegments
            .Select(segment =>
            {
                var fullName = string.IsNullOrWhiteSpace(
                    normalizedParent)
                    ? segment
                    : $"{normalizedParent}.{segment}";
                var directTypeCount = commands
                    .Where(command => string.Equals(
                        command.NamespaceName,
                        fullName,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(command => command.TypeName)
                    .Where(typeName => !string.IsNullOrWhiteSpace(
                        typeName))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                var descendantPrefix = $"{fullName}.";
                var descendantTypeCount = commands
                    .Where(command => command.NamespaceName.StartsWith(
                        descendantPrefix,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(command =>
                        $"{command.NamespaceName}\u001F{command.TypeName}")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                return new DotNetNamespaceDefinition(
                    segment,
                    fullName,
                    directTypeCount,
                    descendantTypeCount);
            })
            .OrderBy(
                option => option.Segment,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private int GetCurrentDirectTypeCount(string namespaceName)
    {
        if (SelectedDotNetApiIndex is { } index)
        {
            return index.GetTypes(namespaceName).Count;
        }

        return GetActiveCSharpCommands()
            .Where(command => string.Equals(
                command.NamespaceName,
                namespaceName,
                StringComparison.OrdinalIgnoreCase))
            .Select(command => command.TypeName)
            .Where(typeName => !string.IsNullOrWhiteSpace(typeName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private void RefreshCSharpTypes()
    {
        var previousName = SelectedCSharpType?.DisplayName;
        _selectedCSharpType = null;
        _selectedCSharpMember = null;
        _selectedCSharpOverload = null;
        CSharpMembers.Clear();
        CSharpOverloads.Clear();
        SelectedCommand = null;

        if (string.IsNullOrWhiteSpace(SelectedNamespacePath))
        {
            CSharpTypes.Clear();
            NotifyCSharpSelectionChanged();
            return;
        }

        var authoredByType = GetActiveCSharpCommands()
            .Where(command => string.Equals(
                command.NamespaceName,
                SelectedNamespacePath,
                StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(command.TypeName))
            .GroupBy(
                command => NormalizeCSharpTypeName(command.TypeName),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CommandDefinition>)group.ToList(),
                StringComparer.OrdinalIgnoreCase);
        var options = new List<CSharpTypeOptionViewModel>();
        var runtimeTypes = SelectedDotNetApiIndex is { } index
            ? index.GetTypes(SelectedNamespacePath)
            : [];
        foreach (var runtimeType in runtimeTypes)
        {
            authoredByType.Remove(
                NormalizeCSharpTypeName(runtimeType.DisplayName),
                out var authored);
            options.Add(new CSharpTypeOptionViewModel(
                runtimeType,
                authored ?? []));
        }

        foreach (var authored in authoredByType.Values)
        {
            options.Add(new CSharpTypeOptionViewModel(
                null,
                authored));
        }

        ReplaceItems(
            CSharpTypes,
            options.OrderBy(
                option => option.DisplayName,
                StringComparer.OrdinalIgnoreCase));
        SelectedCSharpType = CSharpTypes.FirstOrDefault(option =>
            string.Equals(
                option.DisplayName,
                previousName,
                StringComparison.OrdinalIgnoreCase));
        NotifyCSharpSelectionChanged();
    }

    private void RefreshCSharpMembers()
    {
        CSharpMembers.Clear();
        CSharpOverloads.Clear();
        _selectedCSharpOverload = null;
        SelectedCommand = null;
        if (SelectedCSharpType is null)
        {
            NotifyCSharpSelectionChanged();
            return;
        }

        var authoredByMember = SelectedCSharpType.AuthoredCommands
            .GroupBy(
                command => command.MemberName,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CommandDefinition>)group.ToList(),
                StringComparer.OrdinalIgnoreCase);
        var options = new List<CSharpMemberOptionViewModel>();
        if (SelectedCSharpType.RuntimeType is not null)
        {
            foreach (var runtimeMember in SelectedCSharpType.RuntimeType
                         .MemberGroups)
            {
                authoredByMember.Remove(
                    runtimeMember.Name,
                    out var authored);
                options.Add(new CSharpMemberOptionViewModel(
                    runtimeMember,
                    authored ?? []));
            }
        }

        foreach (var authored in authoredByMember.Values)
        {
            options.Add(new CSharpMemberOptionViewModel(null, authored));
        }

        ReplaceItems(
            CSharpMembers,
            options
                .OrderBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(option => option.KindLabel, StringComparer.OrdinalIgnoreCase));
        NotifyCSharpSelectionChanged();
    }

    private void RefreshCSharpOverloads()
    {
        CSharpOverloads.Clear();
        _selectedCSharpOverload = null;
        SelectedCommand = null;
        if (SelectedCSharpMember is null)
        {
            NotifyCSharpSelectionChanged();
            return;
        }

        foreach (var command in SelectedCSharpMember.AuthoredCommands)
        {
            var libraryName = Libraries.FirstOrDefault(library =>
                                  string.Equals(
                                      library.Id,
                                      command.CatalogId,
                                      StringComparison.OrdinalIgnoreCase))
                              ?.DisplayName
                              ?? "Authored library";
            CSharpOverloads.Add(new CSharpOverloadOptionViewModel(
                command,
                null,
                string.IsNullOrWhiteSpace(command.OverloadSignature)
                    ? command.Template
                    : command.OverloadSignature,
                $"Authored · {libraryName}"));
        }

        if (SelectedCSharpMember.RuntimeMember is not null)
        {
            foreach (var overload in SelectedCSharpMember.RuntimeMember
                         .Overloads)
            {
                CSharpOverloads.Add(new CSharpOverloadOptionViewModel(
                    null,
                    overload,
                    overload.Signature,
                    $"{SelectedDotNetApiIndex?.DisplayName ?? ".NET"} · " +
                    overload.SourceLabel));
            }
        }

        SelectedCSharpOverload = CSharpOverloads.FirstOrDefault();
        NotifyCSharpSelectionChanged();
    }

    private void NotifyCSharpSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCSharpType));
        OnPropertyChanged(nameof(SelectedCSharpMember));
        OnPropertyChanged(nameof(SelectedCSharpOverload));
        OnPropertyChanged(nameof(HasSelectedCSharpType));
        OnPropertyChanged(nameof(HasSelectedCSharpMember));
        OnPropertyChanged(nameof(HasSelectedCSharpOverload));
        OnPropertyChanged(nameof(CSharpTypeHint));
        OnPropertyChanged(nameof(CSharpMemberHint));
        OnPropertyChanged(nameof(CSharpOverloadHint));
        OnPropertyChanged(nameof(NamespaceHint));
        OnPropertyChanged(nameof(IsCSharpLanguagePattern));
    }

    private IEnumerable<CommandDefinition> GetActiveCSharpCommands()
    {
        var selectedLibraryId = SelectedBuilderSurface?.LibraryId;
        if (string.IsNullOrWhiteSpace(selectedLibraryId)
            || !LibrarySelections.Any(selection =>
                selection.IsSelected
                && string.Equals(
                    selection.Library.Id,
                    selectedLibraryId,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return [];
        }

        return Commands.Where(command =>
            command.ExecutionTarget == ExecutionTarget.CSharp
            && string.Equals(
                command.CatalogId,
                selectedLibraryId,
                StringComparison.OrdinalIgnoreCase));
    }

    private void OpenDotNetSearchResult(DotNetApiSearchEntry entry)
    {
        SelectBuilderSurface(entry.LibraryId);
        SelectCSharpNamespacePath(entry.Type.NamespaceName);
        SelectedCSharpType = CSharpTypes.FirstOrDefault(option =>
            ReferenceEquals(option.RuntimeType, entry.Type));
        SelectedCSharpMember = CSharpMembers.FirstOrDefault(option =>
            ReferenceEquals(option.RuntimeMember, entry.Member));
        StatusMessage =
            $"Opened {entry.DisplayName} from the installed " +
            $"{entry.FrameworkDisplayName} API index.";
    }

    private void OpenCSharpSearchResult(CommandDefinition command)
    {
        SelectBuilderSurface(command.CatalogId);
        if (string.IsNullOrWhiteSpace(command.NamespaceName)
            || string.IsNullOrWhiteSpace(command.TypeName))
        {
            RefreshCSharpLanguagePatterns();
            SelectedCSharpLanguagePattern =
                CSharpLanguagePatterns.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.Id,
                        command.Id,
                        StringComparison.OrdinalIgnoreCase))
                ?? command;
            OnPropertyChanged(nameof(IsCSharpLanguagePattern));
            StatusMessage =
                $"Opened the {command.DisplayName} language pattern from search.";
            return;
        }

        SelectCSharpNamespacePath(command.NamespaceName);
        SelectedCSharpType = CSharpTypes.FirstOrDefault(option =>
            string.Equals(
                NormalizeCSharpTypeName(option.DisplayName),
                NormalizeCSharpTypeName(command.TypeName),
                StringComparison.OrdinalIgnoreCase));
        SelectedCSharpMember = CSharpMembers.FirstOrDefault(option =>
            option.AuthoredCommands.Contains(command)
            || string.Equals(
                option.RuntimeMember?.Name,
                command.MemberName,
                StringComparison.OrdinalIgnoreCase));
        SelectedCSharpOverload = CSharpOverloads.FirstOrDefault(option =>
            ReferenceEquals(option.AuthoredCommand, command))
            ?? SelectedCSharpOverload;
        StatusMessage = $"Opened {command.DisplayName} from search.";
    }

    private void SelectBuilderSurface(ExecutionTarget target)
    {
        var option = BuilderSurfaces.FirstOrDefault(candidate =>
            candidate.ExecutionTarget == target);
        if (option is not null
            && !ReferenceEquals(option, SelectedBuilderSurface))
        {
            SelectedBuilderSurface = option;
        }
    }

    private void SelectBuilderSurface(string libraryId)
    {
        var option = BuilderSurfaces.FirstOrDefault(candidate =>
            string.Equals(
                candidate.LibraryId,
                libraryId,
                StringComparison.OrdinalIgnoreCase));
        if (option is not null
            && !ReferenceEquals(option, SelectedBuilderSurface))
        {
            SelectedBuilderSurface = option;
        }
    }

    private void SelectCSharpNamespacePath(string namespaceName)
    {
        if (!IsCSharpApiSurface)
        {
            return;
        }

        RefreshCSharpNamespaceRoot();
        var segments = namespaceName.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);
        for (var depth = 0;
             depth < segments.Length && depth < CSharpNamespaceLevels.Count;
             depth++)
        {
            var level = CSharpNamespaceLevels[depth];
            var option = level.Options.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Segment,
                    segments[depth],
                    StringComparison.OrdinalIgnoreCase));
            if (option is null)
            {
                return;
            }

            level.SelectedOption = option;
        }
    }

    private static string NormalizeCSharpTypeName(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character)));
}

public sealed record BuilderSurfaceOption(
    string LibraryId,
    ExecutionTarget ExecutionTarget,
    string DisplayName,
    string Description,
    string GroupLabel,
    CommandLibraryKind Kind,
    bool IncludesRuntimeIndex);

public sealed record DotNetInvocationSyntaxOption(
    ExecutionTarget ExecutionTarget,
    string DisplayName,
    string Description,
    string Example);

public sealed class CSharpNamespaceLevelViewModel : ObservableObject
{
    private CSharpNamespaceOptionViewModel? _selectedOption;

    public CSharpNamespaceLevelViewModel(
        int depth,
        IReadOnlyList<CSharpNamespaceOptionViewModel> options)
    {
        Depth = depth;
        Options = options;
    }

    public int Depth { get; }
    public string Label => Depth == 0
        ? "ROOT"
        : $"LEVEL {Depth + 1}";
    public IReadOnlyList<CSharpNamespaceOptionViewModel> Options { get; }

    public CSharpNamespaceOptionViewModel? SelectedOption
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

public sealed record CSharpNamespaceOptionViewModel(
    string Segment,
    string FullName,
    string CountLabel,
    bool IsParentNamespaceSelection)
{
    public string SelectionHint => IsParentNamespaceSelection
        ? $"Use {FullName}"
        : FullName;

    public static CSharpNamespaceOptionViewModel FromDefinition(
        DotNetNamespaceDefinition definition) =>
        new(
            definition.Segment,
            definition.FullName,
            definition.CountLabel,
            false);

    public static CSharpNamespaceOptionViewModel CreateParentSelection(
        string parentNamespace) =>
        new(
            string.Empty,
            parentNamespace,
            string.Empty,
            true);
}

public sealed class CSharpTypeOptionViewModel
{
    public CSharpTypeOptionViewModel(
        DotNetApiTypeDefinition? runtimeType,
        IReadOnlyList<CommandDefinition> authoredCommands)
    {
        RuntimeType = runtimeType;
        AuthoredCommands = authoredCommands;
        DisplayName = runtimeType?.DisplayName
                      ?? authoredCommands.FirstOrDefault()?.TypeName
                      ?? "Unknown type";
        FullDisplayName = runtimeType?.FullDisplayName
                          ?? DisplayName;
        ContextLabel = runtimeType?.ContextLabel
                       ?? "authored API entry";
        TypeKindLabel = (
            runtimeType?.TypeKind
            ?? "authored type").ToUpperInvariant();
    }

    public DotNetApiTypeDefinition? RuntimeType { get; }
    public IReadOnlyList<CommandDefinition> AuthoredCommands { get; }
    public string DisplayName { get; }
    public string FullDisplayName { get; }
    public string ContextLabel { get; }
    public string TypeKindLabel { get; }
    public bool IsStaticClass => string.Equals(
        RuntimeType?.TypeKind,
        "static class",
        StringComparison.OrdinalIgnoreCase);
    public string UsageHint
    {
        get
        {
            if (RuntimeType is null)
            {
                return "Authored API type supplied by the selected library.";
            }

            return RuntimeType.TypeKind switch
            {
                "static class" =>
                    "Static class: choose a static method or property; no object is required.",
                "abstract class" =>
                    "Abstract class: use a static member or provide an existing derived instance.",
                "interface" =>
                    "Interface: provide an existing implementing object for instance members.",
                "struct" =>
                    "Value type: choose a member or a public constructor when one is exposed.",
                "enum" =>
                    "Enum type: inspect its public fields and static members.",
                "delegate" =>
                    "Delegate type: construct it from a compatible callable expression.",
                _ when RuntimeType.MemberGroups.Any(group =>
                    group.Kind == DotNetApiMemberKind.Constructor) =>
                    "Instance-capable class: choose new to construct an object, " +
                    "or provide an existing object for instance members.",
                _ =>
                    "No public constructor is exposed; use a static member or an existing instance."
            };
        }
    }
}

public sealed class CSharpMemberOptionViewModel
{
    public CSharpMemberOptionViewModel(
        DotNetApiMemberGroup? runtimeMember,
        IReadOnlyList<CommandDefinition> authoredCommands)
    {
        RuntimeMember = runtimeMember;
        AuthoredCommands = authoredCommands;
        DisplayName = runtimeMember?.DisplayName
                      ?? authoredCommands.FirstOrDefault()?.MemberName
                      ?? "Unknown member";
        KindLabel = runtimeMember?.DetailLabel
                    ?? BuildAuthoredLabel(authoredCommands);
    }

    public DotNetApiMemberGroup? RuntimeMember { get; }
    public IReadOnlyList<CommandDefinition> AuthoredCommands { get; }
    public string DisplayName { get; }
    public string KindLabel { get; }

    private static string BuildAuthoredLabel(
        IReadOnlyList<CommandDefinition> commands)
    {
        var kind = commands.FirstOrDefault()?.MemberKind;
        return $"{(string.IsNullOrWhiteSpace(kind) ? "Authored member" : kind)} · " +
               $"{commands.Count:N0} variation{(commands.Count == 1 ? string.Empty : "s")}";
    }
}

public sealed record CSharpOverloadOptionViewModel(
    CommandDefinition? AuthoredCommand,
    DotNetApiOverloadDefinition? RuntimeOverload,
    string Signature,
    string SourceLabel);

public sealed class CommandSearchResultViewModel
{
    private CommandSearchResultViewModel(
        CommandDefinition? command,
        DotNetApiSearchEntry? apiEntry,
        int score)
    {
        Command = command;
        ApiEntry = apiEntry;
        Score = score;
        DisplayName = command?.DisplayName
                      ?? apiEntry?.DisplayName
                      ?? string.Empty;
        ContextLabel = command is not null
            ? $"{command.ModuleName} · {command.Description}"
            : apiEntry?.ContextLabel ?? string.Empty;
        SearchText = command?.SearchText
                     ?? apiEntry?.SearchText
                     ?? string.Empty;
        Key = command is not null
            ? $"command:{command.CatalogId}:{command.Id}"
            : $"api:{apiEntry?.LibraryId}:{apiEntry?.QualifiedMemberName}:" +
              $"{apiEntry?.Member.Kind}";
    }

    public CommandDefinition? Command { get; }
    public DotNetApiSearchEntry? ApiEntry { get; }
    public int Score { get; }
    public string DisplayName { get; }
    public string ContextLabel { get; }
    public string SearchText { get; }
    public string Key { get; }

    public static CommandSearchResultViewModel FromCommand(
        CommandDefinition command,
        int score) =>
        new(command, null, score);

    public static CommandSearchResultViewModel FromApi(
        DotNetApiSearchEntry entry,
        int score) =>
        new(null, entry, score);
}
