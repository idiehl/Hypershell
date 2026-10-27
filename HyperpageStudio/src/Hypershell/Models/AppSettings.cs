using Hypershell.Infrastructure;
using Hypershell.Profiles;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace Hypershell.Models;

public sealed class AppSettings
{
    public int TypographyVersion { get; set; }
    public int InputBehaviorVersion { get; set; }
    public int TerminalProfilesVersion { get; set; }
    public int CommandLibraryLayoutVersion { get; set; }
    public string PortableProfileId { get; set; } =
        Guid.NewGuid().ToString("N");
    public string PortableProfileDisplayName { get; set; } = "Default";
    public long PortableProfileRevision { get; set; }
    public DateTimeOffset PortableProfileUpdatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;
    public ObservableCollection<ProfileExtensionReference> ProfileExtensions
        { get; set; } = [];
    public ObservableCollection<ProfileLibraryReference> ProfileLibraries
        { get; set; } = [];
    public ApplicationPreferences Application { get; set; } = new();
    public CommandBuilderPreferences CommandBuilder { get; set; } = new();
    public ExplorerSettings Explorer { get; set; } = new();
    public WorkspaceLayoutSnapshot WorkspaceLayout { get; set; } = new();
    public TerminalSettings Terminal { get; set; } = new();
    public string ActiveTerminalProfileId { get; set; } = "powershell";
    public ObservableCollection<TerminalProfile> TerminalProfiles { get; set; } = [];
    public ObservableCollection<TerminalConfigurationProfile> TerminalConfigurationProfiles { get; set; } = [];
    public string ActiveEnvironmentId { get; set; } = "powershell";
    public ObservableCollection<EnvironmentProfile> Environments { get; set; } =
    [
        new EnvironmentProfile
        {
            Id = "powershell",
            Name = "PowerShell Default",
            Description = "Built-in PowerShell commands discovered from the embedded engine.",
            Language = "PowerShell",
            LibraryIds = ["powershell-core"],
            SourceLibraryId = "powershell-core"
        },
        new EnvironmentProfile
        {
            Id = "csharp",
            Name = "C# starter environment",
            Description =
                "Installed .NET framework, SDK, and CLI surfaces available to the C# Command Builder.",
            Language = "C#",
            LibraryIds =
            [
                "csharp-dotnet-10",
                "csharp-dotnet-8",
                "csharp-dotnet-sdk",
                "csharp-dotnet-cli"
            ],
            SourceLibraryId = "csharp-dotnet-10"
        }
    ];
}

public enum NewTabPlacement
{
    AfterLast,
    AfterCurrent
}

public sealed class ApplicationPreferences
{
    public bool OpenFileExplorerOnStartup { get; set; } = true;
    public bool OpenCommandBuilderOnStartup { get; set; } = true;
    public bool CreateTerminalOnStartup { get; set; } = true;
    public bool ShowSaveStatusInTitleBar { get; set; } = true;
    public NewTabPlacement NewTabPlacement { get; set; } = NewTabPlacement.AfterLast;

    public ApplicationPreferences Clone() =>
        new()
        {
            OpenFileExplorerOnStartup = OpenFileExplorerOnStartup,
            OpenCommandBuilderOnStartup = OpenCommandBuilderOnStartup,
            CreateTerminalOnStartup = CreateTerminalOnStartup,
            ShowSaveStatusInTitleBar = ShowSaveStatusInTitleBar,
            NewTabPlacement = NewTabPlacement
        };

    public void CopyFrom(ApplicationPreferences source)
    {
        OpenFileExplorerOnStartup = source.OpenFileExplorerOnStartup;
        OpenCommandBuilderOnStartup = source.OpenCommandBuilderOnStartup;
        CreateTerminalOnStartup = source.CreateTerminalOnStartup;
        ShowSaveStatusInTitleBar = source.ShowSaveStatusInTitleBar;
        NewTabPlacement = source.NewTabPlacement;
    }
}

public sealed class CommandBuilderPreferences
{
    public string ActiveLanguage { get; set; } = string.Empty;
    public bool OpenCompatibleTerminalAutomatically { get; set; } = true;
    public bool OpenBuilderWhenPathsAreInserted { get; set; } = true;

    public CommandBuilderPreferences Clone() =>
        new()
        {
            ActiveLanguage = ActiveLanguage,
            OpenCompatibleTerminalAutomatically = OpenCompatibleTerminalAutomatically,
            OpenBuilderWhenPathsAreInserted = OpenBuilderWhenPathsAreInserted
        };

    public void CopyFrom(CommandBuilderPreferences source)
    {
        ActiveLanguage = source.ActiveLanguage;
        OpenCompatibleTerminalAutomatically = source.OpenCompatibleTerminalAutomatically;
        OpenBuilderWhenPathsAreInserted = source.OpenBuilderWhenPathsAreInserted;
    }
}

public sealed class ExplorerSettings
{
    public string StartupDirectory { get; set; } = string.Empty;
    public bool UseAsTerminalStartupDirectory { get; set; }
    public bool ShowHiddenItems { get; set; }
    public bool DirectoriesFirst { get; set; } = true;
    public string ActiveProjectId { get; set; } = string.Empty;
    public ObservableCollection<ProjectDefinition> Projects { get; set; } = [];

    public ExplorerSettings Clone() =>
        new()
        {
            StartupDirectory = StartupDirectory,
            UseAsTerminalStartupDirectory = UseAsTerminalStartupDirectory,
            ShowHiddenItems = ShowHiddenItems,
            DirectoriesFirst = DirectoriesFirst,
            ActiveProjectId = ActiveProjectId,
            Projects = new ObservableCollection<ProjectDefinition>(
                Projects.Select(project => project.Clone()))
        };

    public void CopyFrom(ExplorerSettings source)
    {
        StartupDirectory = source.StartupDirectory;
        UseAsTerminalStartupDirectory = source.UseAsTerminalStartupDirectory;
        ShowHiddenItems = source.ShowHiddenItems;
        DirectoriesFirst = source.DirectoriesFirst;
        ActiveProjectId = source.ActiveProjectId;
        Projects = new ObservableCollection<ProjectDefinition>(
            source.Projects.Select(project => project.Clone()));
    }
}

public sealed class ProjectDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New project";
    public ObservableCollection<string> Directories { get; set; } = [];

    public ProjectDefinition Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Directories = new ObservableCollection<string>(Directories)
        };
}

public sealed class EnvironmentProfile : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = "New environment";
    private string _description = string.Empty;
    private string _language = string.Empty;
    private List<string> _libraryIds = [];
    private string _sourceLibraryId = string.Empty;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string Language
    {
        get => _language;
        set => SetProperty(ref _language, value);
    }

    public List<string> LibraryIds
    {
        get => _libraryIds;
        set => SetProperty(ref _libraryIds, value);
    }

    public string SourceLibraryId
    {
        get => _sourceLibraryId;
        set => SetProperty(ref _sourceLibraryId, value);
    }

    public EnvironmentProfile Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Description = Description,
            Language = Language,
            LibraryIds = [.. LibraryIds],
            SourceLibraryId = SourceLibraryId
        };
}

public enum TerminalShellKind
{
    PowerShell,
    WindowsPowerShell,
    CommandPrompt,
    WslBash,
    GitBash,
    Python,
    NodeJs,
    TypeScript,
    Java,
    Cpp,
    CSharp,
    DockerCli,
    DockerContainer,
    MySql,
    DeveloperPowerShell,
    AzureCloudShell,
    External
}

public sealed class TerminalProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New terminal";
    public TerminalShellKind ShellKind { get; set; }
    public string ExecutablePath { get; set; } = string.Empty;
    public string CompilerExecutablePath { get; set; } = string.Empty;
    public string RuntimeExecutablePath { get; set; } = string.Empty;
    public List<string> Arguments { get; set; } = [];
    public string Distribution { get; set; } = string.Empty;
    public string InitializationCommand { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public TerminalSettings Settings { get; set; } = new();
    public string TabTitleTemplate { get; set; } = string.Empty;
    public string TabColor { get; set; } = string.Empty;
    public string StartupCommand { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsAvailable { get; set; } = true;

    [JsonIgnore]
    public string AvailabilityMessage { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayName => IsAvailable ? Name : $"{Name} — unavailable";

    public TerminalProfile Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            ShellKind = ShellKind,
            ExecutablePath = ExecutablePath,
            CompilerExecutablePath = CompilerExecutablePath,
            RuntimeExecutablePath = RuntimeExecutablePath,
            Arguments = [.. Arguments],
            Distribution = Distribution,
            InitializationCommand = InitializationCommand,
            IsBuiltIn = IsBuiltIn,
            Settings = Settings.Clone(),
            TabTitleTemplate = TabTitleTemplate,
            TabColor = TabColor,
            StartupCommand = StartupCommand,
            IsAvailable = IsAvailable,
            AvailabilityMessage = AvailabilityMessage
        };
}

public sealed class TerminalConfigurationProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New terminal profile";
    public string Description { get; set; } = string.Empty;
    public List<string> TargetTerminalProfileIds { get; set; } = [];
    public TerminalSettings Settings { get; set; } = new();
    public string TabTitleTemplate { get; set; } = "{shell} {n}";
    public string TabColor { get; set; } = "#28313D";
    public string StartupCommand { get; set; } = string.Empty;

    public TerminalConfigurationProfile Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Description = Description,
            TargetTerminalProfileIds = [.. TargetTerminalProfileIds],
            Settings = Settings.Clone(),
            TabTitleTemplate = TabTitleTemplate,
            TabColor = TabColor,
            StartupCommand = StartupCommand
        };
}

public enum TerminalTextBackdropMode
{
    Everything,
    CurrentInput,
    None
}

public sealed class TerminalSettings : ObservableObject
{
    private string _fontFamilyName = "VT323";
    private double _fontSize = 15;
    private bool _useBoldText;
    private bool _useItalicText;
    private string _foregroundColor = "#E6EDF3";
    private string _backgroundColor = "#0D1117";
    private string _accentColor = "#58A6FF";
    private string _errorColor = "#FF7B72";
    private string _backgroundImagePath = string.Empty;
    private bool _clearOnRun;
    private bool _enterRunsCommand = true;
    private bool _showTimestamps;
    private string _startupDirectory = FindInitialStartupDirectory();
    private TerminalTextBackdropMode _textBackdropMode = TerminalTextBackdropMode.CurrentInput;
    private double _textBackdropOpacity = 0.35;

    public string FontFamilyName
    {
        get => _fontFamilyName;
        set
        {
            if (SetProperty(ref _fontFamilyName, value))
            {
                OnPropertyChanged(nameof(TerminalFontFamily));
            }
        }
    }

    public double FontSize
    {
        get => _fontSize;
        set
        {
            if (SetProperty(ref _fontSize, Math.Clamp(value, 8, 36)))
            {
                OnPropertyChanged(nameof(TerminalFontSize));
            }
        }
    }

    public bool UseBoldText
    {
        get => _useBoldText;
        set
        {
            if (SetProperty(ref _useBoldText, value))
            {
                OnPropertyChanged(nameof(TerminalFontWeight));
            }
        }
    }

    public bool UseItalicText
    {
        get => _useItalicText;
        set
        {
            if (SetProperty(ref _useItalicText, value))
            {
                OnPropertyChanged(nameof(TerminalFontStyle));
            }
        }
    }

    public string ForegroundColor
    {
        get => _foregroundColor;
        set
        {
            if (SetProperty(ref _foregroundColor, value))
            {
                OnPropertyChanged(nameof(ForegroundBrush));
            }
        }
    }

    public string BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            if (SetProperty(ref _backgroundColor, value))
            {
                OnPropertyChanged(nameof(BackgroundBrush));
            }
        }
    }

    public string AccentColor
    {
        get => _accentColor;
        set
        {
            if (SetProperty(ref _accentColor, value))
            {
                OnPropertyChanged(nameof(AccentBrush));
            }
        }
    }

    public string ErrorColor
    {
        get => _errorColor;
        set
        {
            if (SetProperty(ref _errorColor, value))
            {
                OnPropertyChanged(nameof(ErrorBrush));
            }
        }
    }

    public string BackgroundImagePath
    {
        get => _backgroundImagePath;
        set => SetProperty(ref _backgroundImagePath, value);
    }

    public bool ClearOnRun
    {
        get => _clearOnRun;
        set => SetProperty(ref _clearOnRun, value);
    }

    public bool EnterRunsCommand
    {
        get => _enterRunsCommand;
        set => SetProperty(ref _enterRunsCommand, value);
    }

    public bool ShowTimestamps
    {
        get => _showTimestamps;
        set => SetProperty(ref _showTimestamps, value);
    }

    public string StartupDirectory
    {
        get => _startupDirectory;
        set => SetProperty(ref _startupDirectory, value);
    }

    public TerminalTextBackdropMode TextBackdropMode
    {
        get => _textBackdropMode;
        set
        {
            var normalizedValue = Enum.IsDefined(value)
                ? value
                : TerminalTextBackdropMode.CurrentInput;
            if (SetProperty(ref _textBackdropMode, normalizedValue))
            {
                OnPropertyChanged(nameof(TranscriptBackdropBrush));
                OnPropertyChanged(nameof(InputBackdropBrush));
                OnPropertyChanged(nameof(InputBackdropBorderBrush));
            }
        }
    }

    public double TextBackdropOpacity
    {
        get => _textBackdropOpacity;
        set
        {
            var normalizedValue = double.IsFinite(value)
                ? Math.Clamp(value, 0, 1)
                : 0.35;
            if (SetProperty(ref _textBackdropOpacity, normalizedValue))
            {
                OnPropertyChanged(nameof(TranscriptBackdropBrush));
                OnPropertyChanged(nameof(InputBackdropBrush));
                OnPropertyChanged(nameof(InputBackdropBorderBrush));
            }
        }
    }

    [JsonIgnore]
    public FontFamily TerminalFontFamily
    {
        get
        {
            try
            {
                if (string.Equals(FontFamilyName, "VT323", StringComparison.OrdinalIgnoreCase)
                    && Application.Current?.TryFindResource("TerminalFontFamily") is FontFamily terminalFontFamily)
                {
                    return terminalFontFamily;
                }

                if (string.Equals(FontFamilyName, "JetBrains Mono", StringComparison.OrdinalIgnoreCase)
                    && Application.Current?.TryFindResource("CodeFontFamily") is FontFamily bundledFontFamily)
                {
                    return bundledFontFamily;
                }

                return new FontFamily(FontFamilyName);
            }
            catch
            {
                return new FontFamily("Consolas");
            }
        }
    }

    [JsonIgnore]
    public FontWeight TerminalFontWeight => UseBoldText ? FontWeights.SemiBold : FontWeights.Normal;

    [JsonIgnore]
    public FontStyle TerminalFontStyle => UseItalicText ? FontStyles.Italic : FontStyles.Normal;

    [JsonIgnore]
    public double TerminalFontSize => FontSize * 96d / 72d;

    [JsonIgnore]
    public Brush ForegroundBrush => ToBrush(ForegroundColor, "#E6EDF3");

    [JsonIgnore]
    public Brush BackgroundBrush => ToBrush(BackgroundColor, "#0D1117");

    [JsonIgnore]
    public Brush AccentBrush => ToBrush(AccentColor, "#58A6FF");

    [JsonIgnore]
    public Brush ErrorBrush => ToBrush(ErrorColor, "#FF7B72");

    [JsonIgnore]
    public Brush TranscriptBackdropBrush =>
        TextBackdropMode == TerminalTextBackdropMode.Everything
            ? CreateBlackBrush(TextBackdropOpacity)
            : Brushes.Transparent;

    [JsonIgnore]
    public Brush InputBackdropBrush =>
        TextBackdropMode == TerminalTextBackdropMode.None
            ? Brushes.Transparent
            : CreateBlackBrush(TextBackdropOpacity);

    [JsonIgnore]
    public Brush InputBackdropBorderBrush =>
        TextBackdropMode == TerminalTextBackdropMode.None
            ? Brushes.Transparent
            : CreateBlackBrush(Math.Min(1, TextBackdropOpacity + 0.12));

    public TerminalSettings Clone() =>
        new()
        {
            FontFamilyName = FontFamilyName,
            FontSize = FontSize,
            UseBoldText = UseBoldText,
            UseItalicText = UseItalicText,
            ForegroundColor = ForegroundColor,
            BackgroundColor = BackgroundColor,
            AccentColor = AccentColor,
            ErrorColor = ErrorColor,
            BackgroundImagePath = BackgroundImagePath,
            ClearOnRun = ClearOnRun,
            EnterRunsCommand = EnterRunsCommand,
            ShowTimestamps = ShowTimestamps,
            StartupDirectory = StartupDirectory,
            TextBackdropMode = TextBackdropMode,
            TextBackdropOpacity = TextBackdropOpacity
        };

    public void CopyFrom(TerminalSettings source)
    {
        FontFamilyName = source.FontFamilyName;
        FontSize = source.FontSize;
        UseBoldText = source.UseBoldText;
        UseItalicText = source.UseItalicText;
        ForegroundColor = source.ForegroundColor;
        BackgroundColor = source.BackgroundColor;
        AccentColor = source.AccentColor;
        ErrorColor = source.ErrorColor;
        BackgroundImagePath = source.BackgroundImagePath;
        ClearOnRun = source.ClearOnRun;
        EnterRunsCommand = source.EnterRunsCommand;
        ShowTimestamps = source.ShowTimestamps;
        StartupDirectory = source.StartupDirectory;
        TextBackdropMode = source.TextBackdropMode;
        TextBackdropOpacity = source.TextBackdropOpacity;
    }

    private static Brush ToBrush(string color, string fallback)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
        }
        catch
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback)!);
        }
    }

    private static Brush CreateBlackBrush(double opacity)
    {
        var alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * byte.MaxValue);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
        brush.Freeze();
        return brush;
    }

    private static string FindInitialStartupDirectory()
    {
        var currentDirectory = Environment.CurrentDirectory;
        if (Directory.Exists(Path.Combine(currentDirectory, "doc")) || Directory.Exists(Path.Combine(currentDirectory, ".git")))
        {
            return currentDirectory;
        }

        var candidate = new DirectoryInfo(AppContext.BaseDirectory);
        while (candidate is not null)
        {
            if (Directory.Exists(Path.Combine(candidate.FullName, "doc")) || Directory.Exists(Path.Combine(candidate.FullName, ".git")))
            {
                return candidate.FullName;
            }

            candidate = candidate.Parent;
        }

        return Directory.Exists(currentDirectory)
            ? currentDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
