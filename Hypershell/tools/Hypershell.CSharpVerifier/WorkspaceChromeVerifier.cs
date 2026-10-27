using System.Xml;
using System.Xml.Linq;

public static class WorkspaceChromeVerifier
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    public static void Verify()
    {
        var mainWindowPath = FindRepositoryPath(
            "src",
            "Hypershell",
            "MainWindow.xaml");
        var document = XDocument.Load(
            mainWindowPath,
            LoadOptions.SetLineInfo);
        var mainWindowText = File.ReadAllText(mainWindowPath);

        var workspaceTabs = FindNamedElement(document, "WorkspaceTabs");
        var explorerReopen = FindNamedElement(
            document,
            "FileExplorerReopenButton");
        var commandBuilderReopen = FindNamedElement(
            document,
            "CommandBuilderReopenButton");
        var accountMenu = FindNamedElement(
            document,
            "ProfileAccountMenuButton");
        var settingsButton = FindNamedElement(document, "SettingsButton");
        var signInItem = FindNamedElement(
            document,
            "ProfileSignInMenuItem");
        var signOutItem = FindNamedElement(
            document,
            "ProfileSignOutMenuItem");
        var loginGate = FindNamedElement(
            document,
            "StartupProfileLoginGate");
        var workspaceDock = FindNamedElement(document, "WorkspaceDock");
        var linkedHostDeviceButton = FindNamedElement(
            document,
            "LinkedHostDeviceButton");

        Require(
            ReferenceEquals(workspaceTabs.Parent, explorerReopen.Parent)
            && ReferenceEquals(workspaceTabs.Parent, commandBuilderReopen.Parent),
            "The pane reopen controls must remain siblings of WorkspaceTabs so every tab type can use them.");
        Require(
            GetAttribute(explorerReopen, "Grid.Column") == "0"
            && GetAttribute(workspaceTabs, "Grid.Column") == "1"
            && GetAttribute(commandBuilderReopen, "Grid.Column") == "2",
            "The universal pane reopen controls no longer flank the shared tab surface.");
        RequireBinding(
            explorerReopen,
            "Command",
            "ToggleFileExplorerCommand");
        RequireBinding(
            explorerReopen,
            "Visibility",
            "IsFileExplorerClosed");
        RequireBinding(
            commandBuilderReopen,
            "Command",
            "ToggleCommandBuilderCommand");
        RequireBinding(
            commandBuilderReopen,
            "Visibility",
            "IsCommandBuilderClosed");
        Require(
            !string.IsNullOrWhiteSpace(
                GetAttribute(explorerReopen, "AutomationProperties.Name"))
            && !string.IsNullOrWhiteSpace(
                GetAttribute(commandBuilderReopen, "AutomationProperties.Name")),
            "The universal pane reopen controls need accessible names.");
        Require(
            GetAttribute(linkedHostDeviceButton, "Click")
                == "OpenLinkedHostsSettingsClick"
            && GetAttribute(linkedHostDeviceButton, "Content") == "+"
            && !string.IsNullOrWhiteSpace(
                GetAttribute(
                    linkedHostDeviceButton,
                    "AutomationProperties.Name")),
            "File Explorer must expose an accessible route to add or manage linked SSH devices beside its device selector.");

        var deviceSelector = document
            .Descendants(Presentation + "ComboBox")
            .Single(element => GetAttribute(element, "ItemsSource")
                .Contains("FileExplorer.Devices", StringComparison.Ordinal));
        var deviceSelectorMarkup = deviceSelector.ToString(
            SaveOptions.DisableFormatting);
        Require(
            deviceSelectorMarkup.Contains(
                "DisplayName",
                StringComparison.Ordinal)
            && deviceSelectorMarkup.Contains(
                "StateText",
                StringComparison.Ordinal)
            && deviceSelectorMarkup.Contains(
                "IsLocal",
                StringComparison.Ordinal)
            && deviceSelectorMarkup.Contains(
                "Visibility\" Value=\"Collapsed",
                StringComparison.Ordinal)
            && !deviceSelectorMarkup.Contains(
                "Choose This PC",
                StringComparison.Ordinal),
            "The Explorer device selector must show the machine name without reserving space for a LOCAL badge.");

        var headerChildren = accountMenu.Parent?.Elements().ToArray()
                             ?? [];
        var accountIndex = Array.IndexOf(headerChildren, accountMenu);
        var settingsIndex = Array.IndexOf(headerChildren, settingsButton);
        Require(
            accountIndex >= 0 && settingsIndex == accountIndex + 1,
            "The account dropdown must remain immediately before the main Settings button.");
        Require(
            GetAttribute(accountMenu, "Click")
                == "OpenProfileAccountMenuClick"
            && GetAttribute(accountMenu, "Width") == "40"
            && GetAttribute(accountMenu, "ToolTip")
                .Contains("ProfileSyncTitleToolTip", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(
                GetAttribute(accountMenu, "AutomationProperties.Name")),
            "The account dropdown lost its click handler, live session status, or accessible name.");
        Require(
            GetAttribute(signInItem, "Header") == "Log in…"
            && GetAttribute(signInItem, "Click")
                == "ShowProfileLoginGateClick"
            && GetAttribute(signInItem, "IsEnabled")
                .Contains("CanProfileSyncSignIn", StringComparison.Ordinal),
            "The signed-out account menu no longer opens the dedicated login gate.");
        Require(
            GetAttribute(signOutItem, "Header") == "Log out"
            && GetAttribute(signOutItem, "Click")
                == "SignOutProfileSyncFromHeaderClick"
            && GetAttribute(signOutItem, "IsEnabled")
                .Contains("CanProfileSyncSignOut", StringComparison.Ordinal),
            "The signed-in account menu no longer exposes the guarded Log out action.");
        var accountMarkup = accountMenu.ToString(
            SaveOptions.DisableFormatting);
        Require(
            !accountMarkup.Contains('\uE70D'),
            "The account button must rely on its familiar avatar affordance without a separate dropdown chevron.");
        foreach (var requiredToken in new[]
                 {
                     "ProfileSyncAccountLabel",
                     "IsProfileSyncSignedIn",
                     "Account &amp; profile sync…",
                     "ChooseProfileImageMenuItem",
                     "RemoveProfileImageMenuItem",
                     "ProfileAvatarInitials",
                     "ProfileAvatarImageSource",
                     "HasProfileAvatarImage"
                 })
        {
            Require(
                accountMarkup.Contains(requiredToken, StringComparison.Ordinal),
                $"The account dropdown lost its {requiredToken} contract.");
        }

        RequireBinding(
            loginGate,
            "Visibility",
            "IsProfileLoginGateVisible");
        Require(
            workspaceDock.ToString(SaveOptions.DisableFormatting)
                .Contains(
                    "IsProfileLoginGateVisible",
                    StringComparison.Ordinal)
            && workspaceDock.ToString(SaveOptions.DisableFormatting)
                .Contains(
                    "Visibility\" Value=\"Collapsed",
                    StringComparison.Ordinal),
            "The workspace must collapse behind the login gate so native WebView surfaces cannot punch through it.");
        var loginGateMarkup = loginGate.ToString(
            SaveOptions.DisableFormatting);
        foreach (var requiredToken in new[]
                 {
                     "BeginProfileLoginClick",
                     "CancelProfileLoginClick",
                     "ContinueWithoutProfileLoginClick",
                     "ProfileLoginStatus",
                     "PendingProfileLoginUri",
                     "CanBeginProfileLogin",
                     "A T L A S   U N I V E R S A L I S",
                     "H Y P E R D E S K",
                     "BROWSER SIGN-IN",
                     "Content=\"Log in\"",
                     "Continue offline",
                     "#FF081226"
                  })
        {
            Require(
                loginGateMarkup.Contains(
                    requiredToken,
                    StringComparison.Ordinal),
                $"The startup login gate lost its {requiredToken} contract.");
        }

        Require(
            !loginGateMarkup.Contains(
                "Welcome to your workspace",
                StringComparison.Ordinal)
            && !loginGateMarkup.Contains(
                "HYPERSHELL ACCOUNT",
                StringComparison.Ordinal)
            && !loginGateMarkup.Contains(
                "rotating credential",
                StringComparison.Ordinal)
            && !loginGateMarkup.Contains(
                "APPROVAL CODE",
                StringComparison.Ordinal),
            "The startup login gate regained the verbose account-card presentation.");

        var composerBlocker = document
            .Descendants(Presentation + "TextBlock")
            .SingleOrDefault(element =>
                GetAttribute(element, "Text")
                    .Contains(
                        "WorkspaceAiAssistant.ComposerBlockerMessage",
                        StringComparison.Ordinal));
        Require(
            composerBlocker is not null
            && MainWindowHasToken(
                document,
                "WorkspaceAiAssistant.EnableSelectedOpenAiCommand")
            && MainWindowHasToken(
                document,
                "WorkspaceAiAssistant.HasComposerBlocker"),
            "The AI composer no longer explains or repairs a disabled provider state.");
        Require(
            mainWindowText.Contains(
                "MODEL FOR THIS CONVERSATION",
                StringComparison.Ordinal)
            && mainWindowText.Contains(
                "WORKING DIRECTORY",
                StringComparison.Ordinal)
            && mainWindowText.Contains(
                "WorkspaceAiAssistant.WorkingDirectoryDisplay",
                StringComparison.Ordinal)
            && mainWindowText.Contains(
                "BrowseWorkspaceAiWorkingDirectoryClick",
                StringComparison.Ordinal)
            && mainWindowText.Contains(
                "ClearWorkspaceAiWorkingDirectoryClick",
                StringComparison.Ordinal)
            && !mainWindowText.Contains(
                "WorkspaceAiAssistant.ProviderStatus",
                StringComparison.Ordinal),
            "The AI pane must expose a per-conversation working directory without restoring the redundant local-ready status line.");

        var contextDialogPath = FindRepositoryPath(
            "src",
            "Hypershell",
            "WorkspaceAiContextDialog.xaml");
        var contextDialogMarkup = File.ReadAllText(contextDialogPath);
        Require(
            !contextDialogMarkup.Contains(
                "DEFAULT DOCUMENT SAVE LOCATION",
                StringComparison.Ordinal)
            && !contextDialogMarkup.Contains(
                "DefaultSaveLocationTextBox",
                StringComparison.Ordinal),
            "AI Context settings must not restore the retired global project-root control.");

        var settingsPath = FindRepositoryPath(
            "src",
            "Hypershell",
            "SettingsWindow.xaml");
        var settingsDocument = XDocument.Load(
            settingsPath,
            LoadOptions.SetLineInfo);
        var settingsMarkup = File.ReadAllText(settingsPath);
        Require(
            settingsMarkup.Contains("Account &amp; Sync", StringComparison.Ordinal)
            && settingsMarkup.Contains("Account Sync", StringComparison.Ordinal)
            && settingsMarkup.Contains("Remote devices (SSH)", StringComparison.Ordinal)
            && !settingsMarkup.Contains("What follows you", StringComparison.Ordinal)
            && !settingsMarkup.Contains("Portable Profiles", StringComparison.Ordinal)
            && !settingsMarkup.Contains("Export .hyperprofile", StringComparison.Ordinal)
            && !settingsMarkup.Contains("Remote profile revisions", StringComparison.Ordinal)
            && !settingsMarkup.Contains("Tombstone remote", StringComparison.Ordinal)
            && !settingsMarkup.Contains("Conflict-copy directory", StringComparison.Ordinal),
            "Settings must expose a compact automatic Account Sync model without explanatory, portable-profile, or revision-management panels.");
        foreach (var readOnlyTextBox in settingsDocument
                     .Descendants(Presentation + "TextBox")
                     .Where(element =>
                         GetAttribute(element, "IsReadOnly") == "True"
                         && GetAttribute(element, "Text")
                             .StartsWith("{Binding", StringComparison.Ordinal)))
        {
            Require(
                GetAttribute(readOnlyTextBox, "Text")
                    .Contains("Mode=OneWay", StringComparison.Ordinal),
                $"Read-only Settings TextBox at line {((IXmlLineInfo)readOnlyTextBox).LineNumber} must use a OneWay binding so opening Settings cannot write into a getter-only property.");
        }

        var updateProgress = settingsDocument
            .Descendants(Presentation + "ProgressBar")
            .SingleOrDefault(element =>
                GetAttribute(element, "Value")
                    .Contains(
                        "ApplicationUpdateProgress",
                        StringComparison.Ordinal));
        Require(
            updateProgress is not null
            && GetAttribute(updateProgress, "Value")
                .Contains("Mode=OneWay", StringComparison.Ordinal),
            "The read-only application-update progress binding can still crash Settings by attempting a TwoWay write.");

        var mainWindowCode = File.ReadAllText(FindRepositoryPath(
            "src",
            "Hypershell",
            "MainWindow.xaml.cs"));
        var settingsWindowCode = File.ReadAllText(FindRepositoryPath(
            "src",
            "Hypershell",
            "SettingsWindow.xaml.cs"));
        Require(
            mainWindowCode.Contains(
                "settingsWindow?.ShowLinkedHosts(beginNewHost: true);",
                StringComparison.Ordinal)
            && settingsWindowCode.Contains(
                "RefreshLinkedHostsForPresentationAsync",
                StringComparison.Ordinal)
            && settingsWindowCode.Contains(
                "CancelAfter(TimeSpan.FromSeconds(15))",
                StringComparison.Ordinal),
            "The Explorer Device add action must open Settings synchronously and refresh linked-host inventory in a bounded background operation.");
        var linkedHostsCard = FindNamedElement(
            settingsDocument,
            "LinkedHostsSettingsCard");
        Require(
            linkedHostsCard.ToString(SaveOptions.DisableFormatting)
                .Contains("appears in File Explorer", StringComparison.Ordinal)
            && settingsDocument
                .Descendants()
                .Any(element =>
                    (string?)element.Attribute(Xaml + "Name")
                    == "LinkedHostDisplayNameBox"),
            "The linked-host settings destination must explain its Explorer relationship and retain a focus target.");

        var liveStyle = document
            .Descendants(Presentation + "Style")
            .SingleOrDefault(element =>
                (string?)element.Attribute(Xaml + "Key")
                == "AiLiveToggleStyle");
        Require(liveStyle is not null,
            "The shared AI live toggle style is missing.");
        var liveStyleMarkup = liveStyle!.ToString(SaveOptions.DisableFormatting);
        foreach (var requiredToken in new[]
                 {
                     "ShellOutline",
                     "ShellPattern",
                     "ShellHead",
                     "PulseHalo",
                     "#FFF6F8FA",
                     "AccentBrush",
                     "IsLiveRequestRunning",
                     "RepeatBehavior=\"Forever\"",
                     "AutoReverse=\"True\""
                 })
        {
            Require(
                liveStyleMarkup.Contains(
                    requiredToken,
                    StringComparison.Ordinal),
                $"The shared AI live toggle lost its {requiredToken} contract.");
        }

        var liveToggles = document
            .Descendants(Presentation + "ToggleButton")
            .Where(element =>
                GetAttribute(element, "Style")
                == "{StaticResource AiLiveToggleStyle}")
            .ToArray();
        Require(liveToggles.Length == 3,
            "Terminal, document, and desktop live assistance must use the same shell toggle.");

        var expectedLiveToggleNames = new HashSet<string>(
            StringComparer.Ordinal)
        {
            "DesktopLiveAssistToggle",
            "TerminalAiLiveAssistToggle",
            "DocumentAiLiveAssistToggle"
        };
        foreach (var toggle in liveToggles)
        {
            var name = (string?)toggle.Attribute(Xaml + "Name")
                       ?? string.Empty;
            Require(expectedLiveToggleNames.Remove(name),
                $"Unexpected AI live toggle '{name}'.");
            Require(toggle.Attribute("Content") is null,
                $"AI live toggle '{name}' regressed to a text label.");
            Require(!string.IsNullOrWhiteSpace(
                    GetAttribute(toggle, "AutomationProperties.Name")),
                $"AI live toggle '{name}' needs an accessible name.");
            Require(!string.IsNullOrWhiteSpace(
                    GetAttribute(toggle, "AutomationProperties.HelpText")),
                $"AI live toggle '{name}' must explain its visual states.");
        }

        Require(expectedLiveToggleNames.Count == 0,
            "One or more expected AI live toggle placements are missing.");
        var mainWindowMarkup = document.ToString(SaveOptions.DisableFormatting);
        Require(
            !mainWindowMarkup.Contains("AI LIVE", StringComparison.Ordinal)
            && !mainWindowMarkup.Contains(
                "DesktopLiveSessionLabel",
                StringComparison.Ordinal),
            "A legacy AI LIVE text label remains in the workspace chrome.");

        Console.WriteLine(
            "WORKSPACE_CHROME_OK|minimal-hyperdesk-login|startup-login-gate|settings-binding-safe|ai-composer-recovery|account-login-dropdown|linked-host-device-entry|universal-pane-reopen|3-shell-live-toggles|active-pulse");
    }

    private static bool MainWindowHasToken(
        XDocument document,
        string token) =>
        document.ToString(SaveOptions.DisableFormatting)
            .Contains(token, StringComparison.Ordinal);

    private static XElement FindNamedElement(
        XDocument document,
        string name)
    {
        return document
                   .Descendants()
                   .SingleOrDefault(element =>
                       (string?)element.Attribute(Xaml + "Name") == name)
               ?? throw new InvalidOperationException(
                   $"MainWindow.xaml is missing '{name}'.");
    }

    private static void RequireBinding(
        XElement element,
        string attributeName,
        string bindingPath)
    {
        var value = GetAttribute(element, attributeName);
        Require(
            value.Contains(bindingPath, StringComparison.Ordinal),
            $"'{attributeName}' on '{(string?)element.Attribute(Xaml + "Name")}' must bind to '{bindingPath}'.");
    }

    private static string GetAttribute(
        XElement element,
        string name)
    {
        return (string?)element.Attribute(name) ?? string.Empty;
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        foreach (var startingPath in new[]
                 {
                     Environment.CurrentDirectory,
                     AppContext.BaseDirectory
                 })
        {
            var directory = new DirectoryInfo(startingPath);
            while (directory is not null)
            {
                var candidate = Path.Combine(
                    new[] { directory.FullName }.Concat(segments).ToArray());
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{Path.Combine(segments)}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
