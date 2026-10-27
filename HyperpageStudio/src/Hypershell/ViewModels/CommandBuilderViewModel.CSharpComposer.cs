using Hypershell.Infrastructure;
using Hypershell.Models;
using Hypershell.Services;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Threading;

namespace Hypershell.ViewModels;

public sealed partial class CommandBuilderViewModel
{
    private const string LiveCSharpComposerCommandId =
        "csharp-composer:live";

    private static readonly IReadOnlyList<CSharpComposerTokenKindOption>
        BaseCSharpComposerKinds =
        [
            new(
                CSharpComposerTokenKind.Keyword,
                "Keyword",
                "Add one of the 130 C# reserved, contextual, preview, or interop keywords."),
            new(
                CSharpComposerTokenKind.Operator,
                "Operator",
                "Add a C# symbolic operator."),
            new(
                CSharpComposerTokenKind.Punctuation,
                "Punctuation",
                "Add punctuation, grouping, a separator, or a statement terminator."),
            new(
                CSharpComposerTokenKind.Layout,
                "Layout",
                "Add spacing, a line break, a blank line, or tab indentation."),
            new(
                CSharpComposerTokenKind.Custom,
                "Value",
                "Enter a variable name, expression, literal, or other source text.")
        ];

    private static readonly IReadOnlyList<CSharpComposerChoice>
        CSharpOperatorChoices = CreateTextChoices(
            "Operator",
            [
                "=", "+", "-", "*", "/", "%", "++", "--",
                "==", "!=", "<", ">", "<=", ">=", "&&", "||", "!",
                "&", "|", "^", "~", "<<", ">>", ">>>",
                "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=",
                "<<=", ">>=", ">>>=", "??", "??=", "?.", "=>", ".."
            ]);

    private static readonly IReadOnlyList<CSharpComposerChoice>
        CSharpPunctuationChoices = CreateTextChoices(
            "Punctuation",
            [
                "(", ")", "{", "}", "[", "]", ";", ",", ".", ":",
                "::", "?", "#"
            ]);

    private static readonly IReadOnlyList<CSharpComposerChoice>
        CSharpLayoutChoices =
        [
            CreateLayoutChoice("Space", " ", "Insert one space."),
            CreateLayoutChoice("Double Space", "  ", "Insert two spaces."),
            CreateLayoutChoice(
                "New Line",
                Environment.NewLine,
                "Start a new line."),
            CreateLayoutChoice(
                "Blank Line",
                Environment.NewLine + Environment.NewLine,
                "Leave one blank line."),
            CreateLayoutChoice("Tab", "\t", "Insert one tab indent.")
        ];

    private readonly ObservableCollection<CSharpKeywordDefinition>
        _csharpKeywordCatalog = [];
    private CSharpComposerTokenKindOption? _selectedCSharpComposerTokenKind;
    private CSharpComposerChoice? _selectedCSharpComposerChoice;
    private CSharpComposerMenuItem? _selectedCSharpComposerMenuItem;
    private string _csharpComposerCustomText = string.Empty;
    private string _csharpComposerMenuText = string.Empty;
    private string _csharpComposerNamespacePath = string.Empty;
    private DotNetApiTypeDefinition? _csharpComposerType;
    private bool _isRebuildingCSharpComposer;
    private bool _isUpdatingCSharpComposerMenu;
    private bool _isCSharpComposerMenuOpen;
    private bool _showCSharpTypePathBackInCategoryMenu;
    private int _csharpTypePathNavigationStartCount;
    private int _csharpComposerMenuReopenVersion;

    public ObservableCollection<CSharpComposerTokenKindOption>
        CSharpComposerTokenKinds { get; } = [];
    public ObservableCollection<CSharpComposerChoice>
        CSharpComposerChoices { get; } = [];
    public ObservableCollection<CSharpComposerMenuItem>
        CSharpComposerMenuItems { get; } = [];
    public ObservableCollection<CSharpComposerTokenViewModel>
        CSharpComposerTokens { get; } = [];

    public RelayCommand AddCSharpComposerCustomTokenCommand { get; private set; }
        = null!;
    public RelayCommand UndoCSharpComposerTokenCommand { get; private set; }
        = null!;
    public RelayCommand ResetCSharpComposerCommand { get; private set; }
        = null!;
    public RelayCommand CommitCSharpComposerMenuTextCommand
        { get; private set; } = null!;

    public bool IsCSharpRecipeSurface =>
        LanguagesMatch(SelectedEnvironmentLanguage, "C#")
        && SelectedSurfaceLibrary is { } library
        && LanguagesMatch(library.Language, "C#")
        && (library.Kind is
            CommandLibraryKind.Package
            or CommandLibraryKind.Module
            or CommandLibraryKind.Extension);
    public bool IsCSharpComposerWorkspace =>
        LanguagesMatch(SelectedEnvironmentLanguage, "C#")
        && !IsCSharpRecipeSurface;
    public bool IsCatalogCommandWorkspace =>
        !IsCSharpComposerWorkspace;
    public bool IsProfileDrivenBuilderWorkspace =>
        IsCatalogCommandWorkspace;
    public bool HasCSharpComposerFramework =>
        IsCSharpComposerWorkspace
        && SelectedSurfaceLibrary is { } library
        && LanguagesMatch(library.Language, "C#")
        && library.Kind != CommandLibraryKind.Language;
    public bool IsCSharpComposerApiFramework =>
        HasCSharpComposerFramework && SelectedDotNetApiIndex is not null;
    public bool IsCSharpComposerToolchain =>
        HasCSharpComposerFramework && IsDotNetToolchainSurface;
    public bool HasCSharpComposerChoices =>
        CSharpComposerChoices.Count > 0;
    public bool UsesCSharpComposerCustomInput =>
        SelectedCSharpComposerTokenKind?.Kind
        == CSharpComposerTokenKind.Custom;
    public bool IsCSharpComposerMenuCustomInput =>
        SelectedCSharpComposerTokenKind?.Kind
        == CSharpComposerTokenKind.Custom;
    public bool HasCSharpComposerTokens => CSharpComposerTokens.Count > 0;
    public bool HasCSharpComposerStatement =>
        !string.IsNullOrWhiteSpace(CSharpComposerStatementText);
    public bool HasStatementPreview =>
        IsCSharpComposerWorkspace
            ? HasCSharpComposerStatement
            : HasSelectedCommand;
    public bool CanAddCurrentStatement =>
        IsCSharpComposerWorkspace
            ? HasCSharpComposerStatement
            : SelectedCommand is not null
              && !IsSelectedCommandMetadataLoading;
    public bool HasCatalogStatementPreview =>
        IsCatalogCommandWorkspace && HasStatementPreview;
    public bool HasProfileDrivenStatementPreview =>
        HasCatalogStatementPreview;

    public string BuilderSurfaceHeading =>
        $"SELECT {GetBuilderSurfaceNoun()}";
    public string BuilderSurfaceToolTip =>
        $"Choose the {SelectedInputSyntax} {GetBuilderSurfaceNoun().ToLowerInvariant()} " +
        "that should supply the next statement.";
    public string SearchSurfaceHeading =>
        $"SEARCH SELECTED {GetBuilderSurfaceNoun()}";

    private string GetBuilderSurfaceNoun()
    {
        if (LanguagesMatch(SelectedInputSyntax, "PowerShell"))
        {
            return "MODULE";
        }

        var kinds = BuilderSurfaces
            .Where(surface => !string.IsNullOrWhiteSpace(surface.LibraryId))
            .Select(surface => surface.Kind)
            .Distinct()
            .ToList();
        if (kinds.Count == 1)
        {
            return kinds[0] switch
            {
                CommandLibraryKind.Framework => "FRAMEWORK",
                CommandLibraryKind.Sdk => "SDK",
                CommandLibraryKind.CommandLineTool => "COMMAND-LINE TOOL",
                CommandLibraryKind.Runtime => "RUNTIME",
                CommandLibraryKind.Core => "CORE LIBRARY",
                CommandLibraryKind.StandardLibrary => "STANDARD LIBRARY",
                CommandLibraryKind.Specification => "SPECIFICATION",
                CommandLibraryKind.PlatformApi => "API LIBRARY",
                CommandLibraryKind.Module => "MODULE",
                CommandLibraryKind.Package => "PACKAGE",
                CommandLibraryKind.Extension => "ADD-ON",
                CommandLibraryKind.Language when LanguagesMatch(
                    SelectedInputSyntax,
                    "Web APIs") => "API LIBRARY",
                CommandLibraryKind.Language when LanguagesMatch(
                    SelectedInputSyntax,
                    "HTML") || LanguagesMatch(SelectedInputSyntax, "CSS") =>
                    "REFERENCE LIBRARY",
                _ => "LIBRARY"
            };
        }

        var languageId = CommandLanguageRegistry.NormalizeId(
            SelectedInputSyntax);
        var hasFramework = kinds.Contains(CommandLibraryKind.Framework);
        var hasTool = kinds.Any(kind => kind is CommandLibraryKind.Sdk
            or CommandLibraryKind.CommandLineTool);
        var hasPackage = kinds.Any(kind => kind is CommandLibraryKind.Module
            or CommandLibraryKind.Package);
        if (languageId == "csharp"
            && hasPackage
            && (hasFramework || hasTool))
        {
            return "LIBRARY, FRAMEWORK, OR TOOL";
        }

        if (languageId == "web" && (hasFramework || hasPackage))
        {
            return "API LIBRARY OR FRAMEWORK";
        }

        if (languageId is "html" or "css" && hasFramework)
        {
            return "LIBRARY OR FRAMEWORK";
        }

        if (hasFramework && !hasTool)
        {
            return "LIBRARY OR FRAMEWORK";
        }

        if (hasFramework || hasTool)
        {
            return "FRAMEWORK OR TOOL";
        }

        if (hasPackage)
        {
            return "LIBRARY OR PACKAGE";
        }

        if (kinds.Contains(CommandLibraryKind.Extension))
        {
            return "LIBRARY OR ADD-ON";
        }

        return languageId switch
        {
            "powershell" => "MODULE",
            "csharp" => "FRAMEWORK OR TOOL",
            "web" => "API LIBRARY",
            "html" or "css" => "REFERENCE LIBRARY",
            _ => "LIBRARY"
        };
    }

    public string CSharpComposerFrameworkSummary
    {
        get
        {
            if (!HasCSharpComposerFramework)
            {
                return "Select a framework or library to begin shaping the statement.";
            }

            if (SelectedDotNetApiIndex is { } index)
            {
                return $"{index.DisplayName} · {index.NamespaceCount:N0} namespaces · " +
                       $"{index.Types.Count:N0} public types · " +
                       $"{index.OverloadCount:N0} callable variations";
            }

            if (SelectedSurfaceLibrary is { } library
                && IsDotNetToolchainSurface)
            {
                return $"{library.DisplayName} · {library.Commands.Count:N0} installed commands";
            }

            return SelectedSurfaceLibrary?.ExecutionSupportMessage
                   ?? BuilderSurfaceDescription;
        }
    }

    public string CSharpComposerPathLabel
    {
        get
        {
            if (_csharpComposerType is not null)
            {
                return _csharpComposerType.FullDisplayName;
            }

            return string.IsNullOrWhiteSpace(_csharpComposerNamespacePath)
                ? "No namespace or type selected"
                : _csharpComposerNamespacePath;
        }
    }

    public string CSharpComposerNextHint
    {
        get
        {
            if (!HasCSharpComposerFramework)
            {
                return "Choose a framework first.";
            }

            if (SelectedCSharpComposerTokenKind is null)
            {
                return IsCSharpComposerApiFramework
                    ? "Choose what to add next. Class / type reveals only valid namespace segments and types at the current path."
                    : IsCSharpComposerToolchain
                        ? "Choose Command to add an installed dotnet operation, or add language tokens around it."
                        : "This framework is catalogued, but no executable API adapter is currently available.";
            }

            return SelectedCSharpComposerTokenKind.Description;
        }
    }

    public string CSharpComposerMenuHeading =>
        SelectedCSharpComposerTokenKind?.Kind switch
        {
            null => "ADD ITEM",
            CSharpComposerTokenKind.TypePath => "SELECT CLASS",
            CSharpComposerTokenKind.Constructor => "SELECT CONSTRUCTOR",
            CSharpComposerTokenKind.Method => "SELECT METHOD",
            CSharpComposerTokenKind.Property => "SELECT PROPERTY",
            CSharpComposerTokenKind.Field => "SELECT FIELD",
            CSharpComposerTokenKind.Event => "SELECT EVENT",
            CSharpComposerTokenKind.Overload => "SELECT OVERLOAD",
            CSharpComposerTokenKind.Command => "SELECT COMMAND",
            CSharpComposerTokenKind.Keyword => "SELECT KEYWORD",
            CSharpComposerTokenKind.Operator => "SELECT OPERATOR",
            CSharpComposerTokenKind.Punctuation => "SELECT PUNCTUATION",
            CSharpComposerTokenKind.Layout => "SELECT LAYOUT",
            CSharpComposerTokenKind.Custom => "ENTER VALUE",
            _ => "SELECT ITEM"
        };

    public CSharpComposerMenuItem? SelectedCSharpComposerMenuItem
    {
        get => _selectedCSharpComposerMenuItem;
        set
        {
            if (_isUpdatingCSharpComposerMenu
                || value is null
                || ReferenceEquals(_selectedCSharpComposerMenuItem, value))
            {
                return;
            }

            _selectedCSharpComposerMenuItem = value;
            OnPropertyChanged();
            if (value.IsBack)
            {
                if (value.UndoesTypePath)
                {
                    NavigateBackOneCSharpTypePathSegment();
                }
                else
                {
                    ReturnToCSharpComposerCategories();
                }
            }
            else if (value.TokenKind is { } tokenKind)
            {
                BeginCSharpComposerMenuSelection(tokenKind);
            }
            else if (value.Choice is { } choice)
            {
                CompleteCSharpComposerMenuSelection(choice);
            }
        }
    }

    public string CSharpComposerMenuText
    {
        get => _csharpComposerMenuText;
        set => SetProperty(
            ref _csharpComposerMenuText,
            value ?? string.Empty);
    }

    public bool IsCSharpComposerMenuOpen
    {
        get => _isCSharpComposerMenuOpen;
        set => SetProperty(ref _isCSharpComposerMenuOpen, value);
    }

    public CSharpComposerTokenKindOption? SelectedCSharpComposerTokenKind
    {
        get => _selectedCSharpComposerTokenKind;
        set
        {
            if (!SetProperty(ref _selectedCSharpComposerTokenKind, value))
            {
                return;
            }

            _selectedCSharpComposerChoice = null;
            OnPropertyChanged(nameof(SelectedCSharpComposerChoice));
            RefreshCSharpComposerChoices();
            OnPropertyChanged(nameof(UsesCSharpComposerCustomInput));
            OnPropertyChanged(nameof(CSharpComposerNextHint));
        }
    }

    public CSharpComposerChoice? SelectedCSharpComposerChoice
    {
        get => _selectedCSharpComposerChoice;
        set
        {
            if (!SetProperty(ref _selectedCSharpComposerChoice, value)
                || value is null)
            {
                return;
            }

            CompleteCSharpComposerMenuSelection(value);
        }
    }

    public string CSharpComposerCustomText
    {
        get => _csharpComposerCustomText;
        set => SetProperty(ref _csharpComposerCustomText, value ?? string.Empty);
    }

    public string CSharpComposerStatementText
    {
        get
        {
            var core = BuildCSharpComposerCore();
            var rendered = new List<CSharpRenderedPiece>();
            var insertedCore = false;
            foreach (var token in CSharpComposerTokens)
            {
                if (token.IsStructural)
                {
                    if (!insertedCore && !string.IsNullOrWhiteSpace(core))
                    {
                        rendered.Add(new CSharpRenderedPiece(
                            core,
                            CSharpComposerTokenKind.StructuredStatement));
                        insertedCore = true;
                    }

                    continue;
                }

                rendered.Add(new CSharpRenderedPiece(token.Text, token.Kind));
            }

            if (!insertedCore && !string.IsNullOrWhiteSpace(core))
            {
                rendered.Add(new CSharpRenderedPiece(
                    core,
                    CSharpComposerTokenKind.StructuredStatement));
            }

            return RenderCSharpPieces(rendered);
        }
    }

    private void InitializeCSharpComposer()
    {
        AddCSharpComposerCustomTokenCommand = new RelayCommand(
            AddCSharpComposerCustomToken);
        UndoCSharpComposerTokenCommand = new RelayCommand(
            UndoCSharpComposerToken);
        ResetCSharpComposerCommand = new RelayCommand(
            () => ResetCSharpComposerState(clearStatus: false));
        CommitCSharpComposerMenuTextCommand = new RelayCommand(
            CommitCSharpComposerMenuText);

        try
        {
            foreach (var keyword in CSharpKeywordCatalogService.Load())
            {
                _csharpKeywordCatalog.Add(keyword);
            }
        }
        catch
        {
            // The load status is surfaced once the user enters the C# workspace.
            // Keeping construction non-fatal preserves the PowerShell builder.
        }

        RefreshCSharpComposerTokenKinds();
        ResetCSharpComposerMenu();
    }

    private void OnCSharpComposerSurfaceChanged()
    {
        if (!IsCSharpComposerWorkspace)
        {
            return;
        }

        ResetCSharpComposerState(clearStatus: true);
        StatusMessage = string.Empty;
    }

    private void ResetCSharpComposerState(bool clearStatus)
    {
        CSharpComposerTokens.Clear();
        CSharpComposerChoices.Clear();
        _selectedCSharpComposerTokenKind = null;
        _selectedCSharpComposerChoice = null;
        _csharpComposerNamespacePath = string.Empty;
        _csharpComposerType = null;
        SelectedCSharpType = null;
        SelectedCSharpMember = null;
        SelectedCSharpOverload = null;
        _showCSharpTypePathBackInCategoryMenu = false;
        _csharpTypePathNavigationStartCount = 0;
        CSharpComposerCustomText = string.Empty;
        if (!_isRebuildingCSharpComposer)
        {
            SelectedCommand = null;
        }

        RefreshCSharpComposerTokenKinds();
        ResetCSharpComposerMenu();
        NotifyCSharpComposerChanged();
        if (!clearStatus)
        {
            StatusMessage = string.Empty;
        }
    }

    private void RefreshCSharpComposerTokenKinds()
    {
        CSharpComposerTokenKinds.Clear();
        if (!IsCSharpComposerWorkspace
            || !HasCSharpComposerFramework)
        {
            return;
        }

        if (IsCSharpComposerApiFramework && _csharpComposerType is null)
        {
            CSharpComposerTokenKinds.Add(new CSharpComposerTokenKindOption(
                CSharpComposerTokenKind.TypePath,
                "Class",
                "Add the next valid namespace segment or a public type at the current namespace level."));
        }

        if (_csharpComposerType is not null)
        {
            AddMemberKindIfAvailable(
                DotNetApiMemberKind.Constructor,
                CSharpComposerTokenKind.Constructor,
                "Constructor");
            AddMemberKindIfAvailable(
                DotNetApiMemberKind.Method,
                CSharpComposerTokenKind.Method,
                "Method");
            AddMemberKindIfAvailable(
                DotNetApiMemberKind.Property,
                CSharpComposerTokenKind.Property,
                "Property");
            AddMemberKindIfAvailable(
                DotNetApiMemberKind.Field,
                CSharpComposerTokenKind.Field,
                "Field");
            AddMemberKindIfAvailable(
                DotNetApiMemberKind.Event,
                CSharpComposerTokenKind.Event,
                "Event");
        }

        if (SelectedCSharpMember?.RuntimeMember is { Overloads.Count: > 1 })
        {
            CSharpComposerTokenKinds.Add(new CSharpComposerTokenKindOption(
                CSharpComposerTokenKind.Overload,
                "Overload",
                "Choose the exact signature to expose its parameters."));
        }

        if (IsCSharpComposerToolchain)
        {
            CSharpComposerTokenKinds.Add(new CSharpComposerTokenKindOption(
                CSharpComposerTokenKind.Command,
                "Command",
                "Add a command discovered from the installed .NET SDK or CLI."));
        }

        foreach (var kind in BaseCSharpComposerKinds)
        {
            CSharpComposerTokenKinds.Add(kind);
        }
    }

    private void AddMemberKindIfAvailable(
        DotNetApiMemberKind memberKind,
        CSharpComposerTokenKind tokenKind,
        string displayName)
    {
        if (_csharpComposerType?.MemberGroups.Any(group =>
                group.Kind == memberKind) != true)
        {
            return;
        }

        CSharpComposerTokenKinds.Add(new CSharpComposerTokenKindOption(
            tokenKind,
            displayName,
            $"Choose a public {displayName.ToLowerInvariant()} declared by " +
            $"{_csharpComposerType.DisplayName}."));
    }

    private void RefreshCSharpComposerChoices()
    {
        CSharpComposerChoices.Clear();
        var kind = SelectedCSharpComposerTokenKind?.Kind;
        switch (kind)
        {
            case CSharpComposerTokenKind.Keyword:
                foreach (var keyword in _csharpKeywordCatalog
                             .OrderBy(
                                 keyword => keyword.CategoryLabel,
                                 StringComparer.OrdinalIgnoreCase)
                             .ThenBy(
                                 keyword => keyword.Keyword,
                                 StringComparer.OrdinalIgnoreCase))
                {
                    CSharpComposerChoices.Add(new CSharpComposerChoice(
                        keyword.Keyword,
                        keyword.Keyword,
                        keyword.DetailLabel,
                        keyword.Description,
                        keyword,
                        "Keyword"));
                }

                break;
            case CSharpComposerTokenKind.Operator:
                AddChoices(CSharpOperatorChoices);
                break;
            case CSharpComposerTokenKind.Punctuation:
                AddChoices(CSharpPunctuationChoices);
                break;
            case CSharpComposerTokenKind.Layout:
                AddChoices(CSharpLayoutChoices);
                break;
            case CSharpComposerTokenKind.TypePath:
                AddCSharpTypePathChoices();
                break;
            case CSharpComposerTokenKind.Constructor:
                AddCSharpMemberChoices(DotNetApiMemberKind.Constructor);
                break;
            case CSharpComposerTokenKind.Method:
                AddCSharpMemberChoices(DotNetApiMemberKind.Method);
                break;
            case CSharpComposerTokenKind.Property:
                AddCSharpMemberChoices(DotNetApiMemberKind.Property);
                break;
            case CSharpComposerTokenKind.Field:
                AddCSharpMemberChoices(DotNetApiMemberKind.Field);
                break;
            case CSharpComposerTokenKind.Event:
                AddCSharpMemberChoices(DotNetApiMemberKind.Event);
                break;
            case CSharpComposerTokenKind.Overload:
                AddCSharpOverloadChoices();
                break;
            case CSharpComposerTokenKind.Command:
                AddCSharpToolchainCommandChoices();
                break;
        }

        OnPropertyChanged(nameof(HasCSharpComposerChoices));
    }

    private void ResetCSharpComposerMenu(bool open = false)
    {
        _isUpdatingCSharpComposerMenu = true;
        try
        {
            _selectedCSharpComposerMenuItem = null;
            CSharpComposerMenuItems.Clear();
            if (_showCSharpTypePathBackInCategoryMenu)
            {
                CSharpComposerMenuItems.Add(new CSharpComposerMenuItem(
                    "Back",
                    null,
                    null,
                    ShowsAddGlyph: false,
                    IsBack: true,
                    UndoesTypePath: true));
            }

            foreach (var tokenKind in CSharpComposerTokenKinds)
            {
                CSharpComposerMenuItems.Add(new CSharpComposerMenuItem(
                    tokenKind.DisplayName,
                    tokenKind,
                    null,
                    ShowsAddGlyph: false));
            }
        }
        finally
        {
            _isUpdatingCSharpComposerMenu = false;
        }

        CSharpComposerMenuText = string.Empty;
        if (open && CSharpComposerMenuItems.Count > 0)
        {
            ReopenCSharpComposerMenu();
        }
        else
        {
            CloseCSharpComposerMenu();
        }

        OnPropertyChanged(nameof(SelectedCSharpComposerMenuItem));
        OnPropertyChanged(nameof(CSharpComposerMenuHeading));
        OnPropertyChanged(nameof(IsCSharpComposerMenuCustomInput));
    }

    private void BeginCSharpComposerMenuSelection(
        CSharpComposerTokenKindOption tokenKind)
    {
        _showCSharpTypePathBackInCategoryMenu = false;
        if (tokenKind.Kind == CSharpComposerTokenKind.TypePath)
        {
            _csharpTypePathNavigationStartCount =
                GetCSharpTypePathTokenCount();
        }

        _selectedCSharpComposerTokenKind = tokenKind;
        _selectedCSharpComposerChoice = null;
        OnPropertyChanged(nameof(SelectedCSharpComposerTokenKind));
        OnPropertyChanged(nameof(SelectedCSharpComposerChoice));
        RefreshCSharpComposerChoices();
        PopulateCSharpComposerChoiceMenu(
            undoTypePathOnBack: false);
        ReopenCSharpComposerMenu();
        StatusMessage = string.Empty;
    }

    private void PopulateCSharpComposerChoiceMenu(
        bool undoTypePathOnBack)
    {
        _isUpdatingCSharpComposerMenu = true;
        try
        {
            _selectedCSharpComposerMenuItem = null;
            CSharpComposerMenuItems.Clear();
            CSharpComposerMenuItems.Add(new CSharpComposerMenuItem(
                "Back",
                null,
                null,
                ShowsAddGlyph: false,
                IsBack: true,
                UndoesTypePath: undoTypePathOnBack));
            foreach (var choice in CSharpComposerChoices)
            {
                CSharpComposerMenuItems.Add(new CSharpComposerMenuItem(
                    choice.DisplayName,
                    null,
                    choice,
                    choice.Payload is DotNetNamespaceDefinition
                        or DotNetApiTypeDefinition));
            }
        }
        finally
        {
            _isUpdatingCSharpComposerMenu = false;
        }

        CSharpComposerMenuText = string.Empty;
        OnPropertyChanged(nameof(SelectedCSharpComposerMenuItem));
        OnPropertyChanged(nameof(CSharpComposerMenuHeading));
        OnPropertyChanged(nameof(IsCSharpComposerMenuCustomInput));
    }

    private void ReturnToCSharpComposerCategories()
    {
        _selectedCSharpComposerChoice = null;
        _selectedCSharpComposerTokenKind = null;
        _showCSharpTypePathBackInCategoryMenu = false;
        CSharpComposerChoices.Clear();
        OnPropertyChanged(nameof(SelectedCSharpComposerChoice));
        OnPropertyChanged(nameof(SelectedCSharpComposerTokenKind));
        ResetCSharpComposerMenu(open: true);
        StatusMessage = string.Empty;
    }

    private void CompleteCSharpComposerMenuSelection(
        CSharpComposerChoice choice)
    {
        ApplyCSharpComposerChoice(choice, recordToken: true);
        _selectedCSharpComposerChoice = null;

        if (choice.Payload is DotNetNamespaceDefinition)
        {
            _selectedCSharpComposerTokenKind =
                CSharpComposerTokenKinds.FirstOrDefault(kind =>
                    kind.Kind == CSharpComposerTokenKind.TypePath);
            OnPropertyChanged(nameof(SelectedCSharpComposerChoice));
            OnPropertyChanged(nameof(SelectedCSharpComposerTokenKind));
            RefreshCSharpComposerChoices();
            PopulateCSharpComposerChoiceMenu(
                undoTypePathOnBack:
                GetCSharpTypePathTokenCount()
                > _csharpTypePathNavigationStartCount);
            StatusMessage = string.Empty;
            NotifyCSharpComposerChanged();
            ReopenCSharpComposerMenu();
            return;
        }

        _showCSharpTypePathBackInCategoryMenu =
            choice.Payload is DotNetApiTypeDefinition
            && GetCSharpTypePathTokenCount()
            > _csharpTypePathNavigationStartCount;
        _selectedCSharpComposerTokenKind = null;
        CSharpComposerChoices.Clear();
        OnPropertyChanged(nameof(SelectedCSharpComposerChoice));
        OnPropertyChanged(nameof(SelectedCSharpComposerTokenKind));
        ResetCSharpComposerMenu(open: true);
        StatusMessage = string.Empty;
        NotifyCSharpComposerChanged();
    }

    private void CloseCSharpComposerMenu()
    {
        _csharpComposerMenuReopenVersion++;
        IsCSharpComposerMenuOpen = false;
    }

    private void ReopenCSharpComposerMenu()
    {
        var reopenVersion = ++_csharpComposerMenuReopenVersion;
        IsCSharpComposerMenuOpen = false;
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                if (reopenVersion != _csharpComposerMenuReopenVersion
                    || !IsCSharpComposerWorkspace
                    || !HasCSharpComposerFramework
                    || CSharpComposerMenuItems.Count == 0)
                {
                    return;
                }

                IsCSharpComposerMenuOpen = true;
            }));
    }

    private void CommitCSharpComposerMenuText()
    {
        var value = CSharpComposerMenuText.Trim();
        if (SelectedCSharpComposerTokenKind?.Kind
            == CSharpComposerTokenKind.Custom)
        {
            CSharpComposerCustomText = value;
            AddCSharpComposerCustomToken();
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var match = CSharpComposerMenuItems.FirstOrDefault(item =>
                        string.Equals(
                            item.DisplayName,
                            value,
                            StringComparison.OrdinalIgnoreCase))
                    ?? CSharpComposerMenuItems.FirstOrDefault(item =>
                        item.DisplayName.StartsWith(
                            value,
                            StringComparison.OrdinalIgnoreCase))
                    ?? CSharpComposerMenuItems.FirstOrDefault(item =>
                        item.DisplayName.Contains(
                            value,
                            StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            StatusMessage = $"No item matches “{value}”.";
            return;
        }

        SelectedCSharpComposerMenuItem = match;
    }

    private void AddChoices(IEnumerable<CSharpComposerChoice> choices)
    {
        foreach (var choice in choices)
        {
            CSharpComposerChoices.Add(choice);
        }
    }

    private void AddCSharpTypePathChoices()
    {
        if (SelectedDotNetApiIndex is not { } index)
        {
            return;
        }

        foreach (var child in index.GetNamespaceChildren(
                     _csharpComposerNamespacePath))
        {
            CSharpComposerChoices.Add(new CSharpComposerChoice(
                $"namespace:{child.FullName}",
                child.Segment,
                $"Namespace · {child.CountLabel}",
                $"Continue the type path through {child.FullName}.",
                child,
                "Namespace"));
        }

        foreach (var type in index.GetTypes(_csharpComposerNamespacePath)
                     .OrderBy(
                         type => type.DisplayName,
                         StringComparer.OrdinalIgnoreCase))
        {
            CSharpComposerChoices.Add(new CSharpComposerChoice(
                $"type:{type.FullDisplayName}",
                type.DisplayName,
                $"{type.TypeKind} · {type.AssemblyName}",
                $"Select the public {type.TypeKind} {type.FullDisplayName}.",
                type,
                "Type"));
        }
    }

    private void AddCSharpMemberChoices(DotNetApiMemberKind memberKind)
    {
        if (_csharpComposerType is null)
        {
            return;
        }

        foreach (var group in _csharpComposerType.MemberGroups
                     .Where(group => group.Kind == memberKind)
                     .OrderBy(
                         group => group.DisplayName,
                         StringComparer.OrdinalIgnoreCase))
        {
            CSharpComposerChoices.Add(new CSharpComposerChoice(
                $"member:{memberKind}:{group.Name}",
                group.DisplayName,
                group.DetailLabel,
                $"{group.InvocationKindLabel} {memberKind.ToString().ToLowerInvariant()} " +
                $"with {group.Overloads.Count:N0} callable variation" +
                $"{(group.Overloads.Count == 1 ? string.Empty : "s")}.",
                group,
                memberKind.ToString()));
        }
    }

    private void AddCSharpOverloadChoices()
    {
        if (SelectedCSharpMember?.RuntimeMember is not { } member)
        {
            return;
        }

        foreach (var overload in member.Overloads)
        {
            CSharpComposerChoices.Add(new CSharpComposerChoice(
                $"overload:{overload.Signature}:{overload.Operation}",
                overload.Signature,
                overload.SourceLabel,
                $"Generate this {overload.InvocationKindLabel} signature.",
                overload,
                "Overload"));
        }
    }

    private void AddCSharpToolchainCommandChoices()
    {
        if (SelectedSurfaceLibrary is not { } library)
        {
            return;
        }

        foreach (var command in library.Commands
                     .OrderBy(
                         command => command.ModuleName,
                         StringComparer.OrdinalIgnoreCase)
                     .ThenBy(
                         command => command.DisplayName,
                         StringComparer.OrdinalIgnoreCase))
        {
            CSharpComposerChoices.Add(new CSharpComposerChoice(
                $"command:{command.Id}",
                command.DisplayName,
                string.IsNullOrWhiteSpace(command.ModuleName)
                    ? library.DisplayName
                    : command.ModuleName,
                command.Description,
                command,
                "Command"));
        }
    }

    private void ApplyCSharpComposerChoice(
        CSharpComposerChoice choice,
        bool recordToken)
    {
        switch (choice.Payload)
        {
            case CSharpKeywordDefinition keyword:
                AddCSharpComposerToken(
                    CSharpComposerTokenKind.Keyword,
                    keyword.Keyword,
                    keyword.DetailLabel,
                    keyword.Description,
                    keyword,
                    isStructural: false,
                    recordToken);
                break;
            case DotNetNamespaceDefinition namespaceDefinition:
                _csharpComposerNamespacePath = namespaceDefinition.FullName;
                AddCSharpComposerToken(
                    CSharpComposerTokenKind.TypePath,
                    namespaceDefinition.Segment,
                    "Namespace",
                    namespaceDefinition.FullName,
                    namespaceDefinition,
                    isStructural: true,
                    recordToken);
                break;
            case DotNetApiTypeDefinition type:
                _csharpComposerType = type;
                _csharpComposerNamespacePath = type.NamespaceName;
                SelectedNamespacePath = type.NamespaceName;
                SelectedCSharpType = new CSharpTypeOptionViewModel(type, []);
                AddCSharpComposerToken(
                    CSharpComposerTokenKind.TypePath,
                    type.DisplayName,
                    type.TypeKind,
                    type.FullDisplayName,
                    type,
                    isStructural: true,
                    recordToken);
                break;
            case DotNetApiMemberGroup member:
                if (recordToken)
                {
                    RemoveCSharpComposerTokens(
                        CSharpComposerTokenKind.Constructor,
                        CSharpComposerTokenKind.Method,
                        CSharpComposerTokenKind.Property,
                        CSharpComposerTokenKind.Field,
                        CSharpComposerTokenKind.Event,
                        CSharpComposerTokenKind.Overload);
                }

                SelectedCSharpMember = new CSharpMemberOptionViewModel(
                    member,
                    []);
                AddCSharpComposerToken(
                    MapMemberKind(member.Kind),
                    member.DisplayName,
                    member.InvocationKindLabel,
                    member.DetailLabel,
                    member,
                    isStructural: true,
                    recordToken);
                break;
            case DotNetApiOverloadDefinition overload:
                if (recordToken)
                {
                    RemoveCSharpComposerTokens(
                        CSharpComposerTokenKind.Overload);
                }

                SelectCSharpComposerOverload(overload);
                AddCSharpComposerToken(
                    CSharpComposerTokenKind.Overload,
                    overload.Signature,
                    overload.SourceLabel,
                    overload.Signature,
                    overload,
                    isStructural: true,
                    recordToken);
                break;
            case CommandDefinition command:
                if (recordToken)
                {
                    RemoveCSharpComposerTokens(
                        CSharpComposerTokenKind.Command);
                }

                SelectedCommand = command;
                AddCSharpComposerToken(
                    CSharpComposerTokenKind.Command,
                    command.DisplayName,
                    command.ModuleName,
                    command.Description,
                    command,
                    isStructural: true,
                    recordToken);
                break;
            case string text:
                AddCSharpComposerToken(
                    SelectedCSharpComposerTokenKind?.Kind
                    ?? CSharpComposerTokenKind.Custom,
                    text,
                    choice.GroupLabel,
                    choice.Description,
                    text,
                    isStructural: false,
                    recordToken);
                break;
        }

        RefreshCSharpComposerTokenKinds();
    }

    private void RemoveCSharpComposerTokens(
        params CSharpComposerTokenKind[] kinds)
    {
        var kindSet = kinds.ToHashSet();
        for (var index = CSharpComposerTokens.Count - 1;
             index >= 0;
             index--)
        {
            if (kindSet.Contains(CSharpComposerTokens[index].Kind))
            {
                CSharpComposerTokens.RemoveAt(index);
            }
        }
    }

    private void AddCSharpComposerToken(
        CSharpComposerTokenKind kind,
        string text,
        string detailLabel,
        string description,
        object payload,
        bool isStructural,
        bool recordToken)
    {
        if (!recordToken)
        {
            return;
        }

        CSharpComposerTokens.Add(new CSharpComposerTokenViewModel(
            kind,
            text,
            detailLabel,
            description,
            payload,
            isStructural));
    }

    private void SelectCSharpComposerOverload(
        DotNetApiOverloadDefinition overload)
    {
        var option = CSharpOverloads.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.RuntimeOverload, overload));
        if (option is null)
        {
            option = new CSharpOverloadOptionViewModel(
                null,
                overload,
                overload.Signature,
                $"{SelectedDotNetApiIndex?.DisplayName ?? ".NET"} · " +
                overload.SourceLabel);
        }

        SelectedCSharpOverload = option;
    }

    private void AddCSharpComposerCustomToken()
    {
        var value = CSharpComposerCustomText.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            StatusMessage =
                "Enter an identifier, value, literal, or expression before adding it.";
            return;
        }

        CSharpComposerTokens.Add(new CSharpComposerTokenViewModel(
            CSharpComposerTokenKind.Custom,
            value,
            "Custom source",
            "User-authored source text.",
            value,
            IsStructural: false));
        CSharpComposerCustomText = string.Empty;
        _selectedCSharpComposerTokenKind = null;
        OnPropertyChanged(nameof(SelectedCSharpComposerTokenKind));
        CSharpComposerChoices.Clear();
        RefreshCSharpComposerTokenKinds();
        ResetCSharpComposerMenu(open: true);
        StatusMessage = string.Empty;
        NotifyCSharpComposerChanged();
    }

    private int GetCSharpTypePathTokenCount() =>
        CSharpComposerTokens.Count(token =>
            token.Kind == CSharpComposerTokenKind.TypePath);

    private void NavigateBackOneCSharpTypePathSegment()
    {
        var typePathTokenCount = GetCSharpTypePathTokenCount();
        if (typePathTokenCount <= _csharpTypePathNavigationStartCount)
        {
            ReturnToCSharpComposerCategories();
            return;
        }

        var tokenIndex = -1;
        for (var index = CSharpComposerTokens.Count - 1;
             index >= 0;
             index--)
        {
            if (CSharpComposerTokens[index].Kind
                == CSharpComposerTokenKind.TypePath)
            {
                tokenIndex = index;
                break;
            }
        }

        if (tokenIndex < 0)
        {
            ReturnToCSharpComposerCategories();
            return;
        }

        var remaining = CSharpComposerTokens
            .Where((_, index) => index != tokenIndex)
            .ToArray();
        RebuildCSharpComposerTokens(remaining);
        _showCSharpTypePathBackInCategoryMenu = false;
        RefreshCSharpComposerTokenKinds();
        _selectedCSharpComposerTokenKind =
            CSharpComposerTokenKinds.FirstOrDefault(kind =>
                kind.Kind == CSharpComposerTokenKind.TypePath);
        _selectedCSharpComposerChoice = null;
        OnPropertyChanged(nameof(SelectedCSharpComposerTokenKind));
        OnPropertyChanged(nameof(SelectedCSharpComposerChoice));
        RefreshCSharpComposerChoices();
        PopulateCSharpComposerChoiceMenu(
            undoTypePathOnBack:
            GetCSharpTypePathTokenCount()
            > _csharpTypePathNavigationStartCount);
        StatusMessage = string.Empty;
        NotifyCSharpComposerChanged();
        ReopenCSharpComposerMenu();
    }

    private void UndoCSharpComposerToken()
    {
        if (CSharpComposerTokens.Count == 0)
        {
            StatusMessage = "There is no C# token to undo.";
            return;
        }

        var remaining = CSharpComposerTokens
            .Take(CSharpComposerTokens.Count - 1)
            .ToArray();
        RebuildCSharpComposerTokens(remaining);
        _showCSharpTypePathBackInCategoryMenu = false;
        RefreshCSharpComposerTokenKinds();
        ResetCSharpComposerMenu();
        NotifyCSharpComposerChanged();
        StatusMessage = string.Empty;
    }

    private void RebuildCSharpComposerTokens(
        IReadOnlyList<CSharpComposerTokenViewModel> tokens)
    {
        _isRebuildingCSharpComposer = true;
        try
        {
            CSharpComposerTokens.Clear();
            _csharpComposerNamespacePath = string.Empty;
            _csharpComposerType = null;
            SelectedCommand = null;
            ClearCSharpNavigation();
            foreach (var token in tokens)
            {
                RestoreCSharpComposerToken(token);
            }
        }
        finally
        {
            _isRebuildingCSharpComposer = false;
        }
    }

    private void RestoreCSharpComposerToken(CSharpComposerTokenViewModel token)
    {
        CSharpComposerTokens.Add(token);
        switch (token.Payload)
        {
            case DotNetNamespaceDefinition namespaceDefinition:
                _csharpComposerNamespacePath = namespaceDefinition.FullName;
                break;
            case DotNetApiTypeDefinition type:
                _csharpComposerType = type;
                _csharpComposerNamespacePath = type.NamespaceName;
                SelectedNamespacePath = type.NamespaceName;
                SelectedCSharpType = new CSharpTypeOptionViewModel(type, []);
                break;
            case DotNetApiMemberGroup member:
                SelectedCSharpMember = new CSharpMemberOptionViewModel(
                    member,
                    []);
                break;
            case DotNetApiOverloadDefinition overload:
                SelectCSharpComposerOverload(overload);
                break;
            case CommandDefinition command:
                SelectedCommand = command;
                break;
        }
    }

    private string BuildCSharpComposerCore()
    {
        if (SelectedCommand is not null)
        {
            var statement = BuildStatement(SelectedCommand).Trim();
            if (SelectedCSharpMember?.RuntimeMember?.Kind
                != DotNetApiMemberKind.Constructor)
            {
                statement = StripGeneratedResultAssignment(statement);
            }

            return statement;
        }

        if (_csharpComposerType is not null)
        {
            return SelectedDotNetInvocationSyntax.ExecutionTarget
                   == ExecutionTarget.PowerShell
                ? $"[{_csharpComposerType.FullDisplayName}]"
                : _csharpComposerType.FullDisplayName;
        }

        return _csharpComposerNamespacePath;
    }

    private void AddCurrentCSharpComposerStatement()
    {
        if (!HasCSharpComposerStatement)
        {
            StatusMessage =
                "Build a statement before sending it to the pipeline.";
            return;
        }

        SynchronizeCSharpComposerPipeline();
        StatusMessage = string.Empty;
    }

    private CommandDefinition CreateCSharpComposerCommand(
        string statement,
        string? commandId = null)
    {
        var executionTarget = ResolveCSharpComposerExecutionTarget();
        return new CommandDefinition
        {
            Id = commandId ?? $"csharp-composer:{Guid.NewGuid():N}",
            CatalogId = SelectedSurfaceLibrary?.Id ?? "csharp-composer",
            Name = "Composed C# statement",
            DisplayName = "Composed C# statement",
            Description =
                "Statement assembled interactively in the C# Command Builder.",
            ModuleName = SelectedSurfaceLibrary?.DisplayName ?? "C#",
            Template = statement,
            ExecutionTarget = executionTarget,
            PreferredShellKind = executionTarget == ExecutionTarget.CSharp
                ? TerminalShellKind.CSharp
                : TerminalShellKind.PowerShell,
            CompositionMode = CommandCompositionMode.Statement,
            IsExecutionSupported =
                SelectedSurfaceLibrary?.IsExecutionSupported ?? true,
            ExecutionSupportMessage =
                SelectedSurfaceLibrary?.ExecutionSupportMessage
                ?? string.Empty
        };
    }

    private void SynchronizeCSharpComposerPipeline()
    {
        if (!IsCSharpComposerWorkspace || _isRebuildingCSharpComposer)
        {
            return;
        }

        var existingIndex = -1;
        for (var index = 0; index < Pipeline.Count; index++)
        {
            if (string.Equals(
                    Pipeline[index].CommandId,
                    LiveCSharpComposerCommandId,
                    StringComparison.Ordinal))
            {
                existingIndex = index;
                break;
            }
        }

        var statement = CSharpComposerStatementText;
        if (string.IsNullOrWhiteSpace(statement))
        {
            if (existingIndex >= 0)
            {
                Pipeline.RemoveAt(existingIndex);
                RebuildCommandText();
            }

            return;
        }

        var command = CreateCSharpComposerCommand(
            statement,
            LiveCSharpComposerCommandId);
        var segment = new CommandSegmentViewModel(
            statement,
            command,
            null,
            "C#");
        if (existingIndex >= 0)
        {
            if (string.Equals(
                    Pipeline[existingIndex].Text,
                    statement,
                    StringComparison.Ordinal)
                && Pipeline[existingIndex].ExecutionTarget
                == command.ExecutionTarget)
            {
                return;
            }

            Pipeline[existingIndex] = segment;
        }
        else
        {
            Pipeline.Add(segment);
        }

        RebuildCommandText();
    }

    private ExecutionTarget ResolveCSharpComposerExecutionTarget()
    {
        if (SelectedCommand is not null)
        {
            return SelectedCommand.ExecutionTarget;
        }

        return CSharpComposerTokens.Any(token => token.IsStructural)
               && IsCSharpComposerApiFramework
            ? SelectedDotNetInvocationSyntax.ExecutionTarget
            : ExecutionTarget.CSharp;
    }

    private void OpenCSharpComposerApiEntry(DotNetApiSearchEntry entry)
    {
        SelectBuilderSurface(entry.LibraryId);
        ResetCSharpComposerState(clearStatus: true);

        var runningPath = string.Empty;
        foreach (var segment in entry.Type.NamespaceName.Split(
                     '.',
                     StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            runningPath = string.IsNullOrWhiteSpace(runningPath)
                ? segment
                : $"{runningPath}.{segment}";
            CSharpComposerTokens.Add(new CSharpComposerTokenViewModel(
                CSharpComposerTokenKind.TypePath,
                segment,
                "Namespace",
                runningPath,
                new DotNetNamespaceDefinition(
                    segment,
                    runningPath,
                    0,
                    0),
                IsStructural: true));
        }

        _csharpComposerNamespacePath = entry.Type.NamespaceName;
        _csharpComposerType = entry.Type;
        CSharpComposerTokens.Add(new CSharpComposerTokenViewModel(
            CSharpComposerTokenKind.TypePath,
            entry.Type.DisplayName,
            entry.Type.TypeKind,
            entry.Type.FullDisplayName,
            entry.Type,
            IsStructural: true));
        SelectedNamespacePath = entry.Type.NamespaceName;
        SelectedCSharpType = new CSharpTypeOptionViewModel(entry.Type, []);
        SelectedCSharpMember = new CSharpMemberOptionViewModel(
            entry.Member,
            []);
        CSharpComposerTokens.Add(new CSharpComposerTokenViewModel(
            MapMemberKind(entry.Member.Kind),
            entry.Member.DisplayName,
            entry.Member.InvocationKindLabel,
            entry.Member.DetailLabel,
            entry.Member,
            IsStructural: true));
        RefreshCSharpComposerTokenKinds();
        ResetCSharpComposerMenu();
        NotifyCSharpComposerChanged();
        StatusMessage = string.Empty;
    }

    private void OpenCSharpComposerCommand(CommandDefinition command)
    {
        SelectBuilderSurface(command.CatalogId);
        ResetCSharpComposerState(clearStatus: true);
        SelectedCommand = command;
        CSharpComposerTokens.Add(new CSharpComposerTokenViewModel(
            CSharpComposerTokenKind.Command,
            command.DisplayName,
            command.ModuleName,
            command.Description,
            command,
            IsStructural: true));
        RefreshCSharpComposerTokenKinds();
        ResetCSharpComposerMenu();
        NotifyCSharpComposerChanged();
        StatusMessage = string.Empty;
    }

    private void NotifyCSharpComposerChanged()
    {
        OnPropertyChanged(nameof(HasCSharpComposerFramework));
        OnPropertyChanged(nameof(IsCSharpComposerApiFramework));
        OnPropertyChanged(nameof(IsCSharpComposerToolchain));
        OnPropertyChanged(nameof(HasCSharpComposerChoices));
        OnPropertyChanged(nameof(HasCSharpComposerTokens));
        OnPropertyChanged(nameof(HasCSharpComposerStatement));
        OnPropertyChanged(nameof(HasStatementPreview));
        OnPropertyChanged(nameof(HasProfileDrivenStatementPreview));
        OnPropertyChanged(nameof(CanAddCurrentStatement));
        OnPropertyChanged(nameof(CSharpComposerFrameworkSummary));
        OnPropertyChanged(nameof(CSharpComposerPathLabel));
        OnPropertyChanged(nameof(CSharpComposerNextHint));
        OnPropertyChanged(nameof(CSharpComposerStatementText));
        OnPropertyChanged(nameof(UsesCSharpComposerCustomInput));
        OnPropertyChanged(nameof(CSharpComposerMenuHeading));
        OnPropertyChanged(nameof(IsCSharpComposerMenuCustomInput));
        AddStatementCommand.RaiseCanExecuteChanged();
        RefreshStagedStatement();
        SynchronizeCSharpComposerPipeline();
    }

    private static CSharpComposerTokenKind MapMemberKind(
        DotNetApiMemberKind memberKind) =>
        memberKind switch
        {
            DotNetApiMemberKind.Constructor =>
                CSharpComposerTokenKind.Constructor,
            DotNetApiMemberKind.Method => CSharpComposerTokenKind.Method,
            DotNetApiMemberKind.Property => CSharpComposerTokenKind.Property,
            DotNetApiMemberKind.Field => CSharpComposerTokenKind.Field,
            DotNetApiMemberKind.Event => CSharpComposerTokenKind.Event,
            _ => CSharpComposerTokenKind.StructuredStatement
        };

    private static IReadOnlyList<CSharpComposerChoice> CreateTextChoices(
        string groupLabel,
        IEnumerable<string> values) =>
        values.Select(value => new CSharpComposerChoice(
                $"{groupLabel}:{value}",
                value,
                groupLabel,
                $"Add {value} to the statement.",
                value,
                groupLabel))
            .ToArray();

    private static CSharpComposerChoice CreateLayoutChoice(
        string displayName,
        string value,
        string description) =>
        new(
            $"Layout:{displayName}",
            displayName,
            "Layout",
            description,
            value,
            "Layout");

    private static string RenderCSharpPieces(
        IReadOnlyList<CSharpRenderedPiece> pieces)
    {
        var builder = new StringBuilder();
        CSharpRenderedPiece? previous = null;
        foreach (var piece in pieces)
        {
            if (piece.Kind == CSharpComposerTokenKind.Layout)
            {
                builder.Append(piece.Text);
                previous = piece;
                continue;
            }

            if (string.IsNullOrWhiteSpace(piece.Text))
            {
                continue;
            }

            var text = piece.Text.Trim();
            if (piece.Kind == CSharpComposerTokenKind.Punctuation
                && text == ";"
                && builder.ToString().TrimEnd().EndsWith(
                    ';'))
            {
                previous = piece;
                continue;
            }

            if (builder.Length > 0
                && !SuppressSpaceBefore(piece)
                && (previous is null || !SuppressSpaceAfter(previous)))
            {
                builder.Append(' ');
            }

            builder.Append(text);
            previous = piece;
        }

        return builder.ToString();
    }

    private static string StripGeneratedResultAssignment(string statement)
    {
        foreach (var prefix in new[]
                 {
                     "$result = ",
                     "$value = ",
                     "var result = ",
                     "var value = "
                 })
        {
            if (statement.StartsWith(
                    prefix,
                    StringComparison.Ordinal))
            {
                return statement[prefix.Length..];
            }
        }

        return statement;
    }

    private static bool SuppressSpaceBefore(CSharpRenderedPiece piece) =>
        piece.Kind == CSharpComposerTokenKind.Punctuation
        && piece.Text is ")" or "]" or "}" or ";" or "," or "."
            or ":" or "::" or "?" or "(" or "[";

    private static bool SuppressSpaceAfter(CSharpRenderedPiece piece) =>
        piece.Kind == CSharpComposerTokenKind.Layout
        || (piece.Kind == CSharpComposerTokenKind.Punctuation
            && piece.Text is "(" or "[" or "{" or "." or "::" or "#");

    private sealed record CSharpRenderedPiece(
        string Text,
        CSharpComposerTokenKind Kind);
}

public enum CSharpComposerTokenKind
{
    Keyword,
    TypePath,
    Constructor,
    Method,
    Property,
    Field,
    Event,
    Overload,
    Operator,
    Punctuation,
    Layout,
    Command,
    Custom,
    StructuredStatement
}

public sealed record CSharpComposerTokenKindOption(
    CSharpComposerTokenKind Kind,
    string DisplayName,
    string Description);

public sealed record CSharpComposerChoice(
    string Key,
    string DisplayName,
    string DetailLabel,
    string Description,
    object Payload,
    string GroupLabel);

public sealed record CSharpComposerMenuItem(
    string DisplayName,
    CSharpComposerTokenKindOption? TokenKind,
    CSharpComposerChoice? Choice,
    bool ShowsAddGlyph,
    bool IsBack = false,
    bool UndoesTypePath = false);

public sealed record CSharpComposerTokenViewModel(
    CSharpComposerTokenKind Kind,
    string Text,
    string DetailLabel,
    string Description,
    object Payload,
    bool IsStructural);
