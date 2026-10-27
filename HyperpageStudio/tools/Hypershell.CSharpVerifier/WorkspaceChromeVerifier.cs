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
        var mainWindowCodeText = File.ReadAllText(FindRepositoryPath(
            "src",
            "Hypershell",
            "MainWindow.xaml.cs"));
        var appDocument = XDocument.Load(
            FindRepositoryPath("src", "Hypershell", "App.xaml"),
            LoadOptions.SetLineInfo);
        var studioDocument = XDocument.Load(
            FindRepositoryPath(
                "src",
                "Hypershell",
                "VisualPageStudioView.xaml"),
            LoadOptions.SetLineInfo);

        var workspaceSurface = FindNamedElement(document, "WorkspaceSurface");
        var workspaceTabs = FindNamedElement(document, "WorkspaceTabs");
        var explorerReopen = FindNamedElement(
            document,
            "FileExplorerReopenButton");
        var commandBuilderReopen = FindNamedElement(
            document,
            "CommandBuilderReopenButton");
        var explorerEdgeRevealHost = FindNamedElement(
            document,
            "FileExplorerEdgeRevealHost");
        var commandBuilderEdgeRevealHost = FindNamedElement(
            document,
            "CommandBuilderEdgeRevealHost");
        var explorerHeaderToggle = FindNamedElement(
            document,
            "FileExplorerHeaderToggle");
        var commandBuilderHeaderToggle = FindNamedElement(
            document,
            "CommandBuilderHeaderToggle");
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
            ReferenceEquals(workspaceSurface, workspaceTabs.Parent)
            && ReferenceEquals(workspaceTabs.Parent, explorerEdgeRevealHost.Parent)
            && ReferenceEquals(
                workspaceTabs.Parent,
                commandBuilderEdgeRevealHost.Parent)
            && ReferenceEquals(explorerReopen.Parent, explorerEdgeRevealHost)
            && ReferenceEquals(
                commandBuilderReopen.Parent,
                commandBuilderEdgeRevealHost),
            "The pane reopen controls must remain inside the shared workspace surface.");
        RequireBinding(
            explorerReopen,
            "Command",
            "ToggleFileExplorerCommand");
        RequireBinding(
            explorerHeaderToggle,
            "Command",
            "ToggleFileExplorerCommand");
        RequireBinding(
            explorerEdgeRevealHost,
            "Visibility",
            "ShouldShowFileExplorerReopenButton");
        RequireBinding(
            commandBuilderReopen,
            "Command",
            "ToggleCommandBuilderCommand");
        RequireBinding(
            commandBuilderHeaderToggle,
            "Command",
            "ToggleCommandBuilderCommand");
        RequireBinding(
            commandBuilderEdgeRevealHost,
            "Visibility",
            "ShouldShowCommandBuilderReopenButton");
        Require(
            GetAttribute(explorerEdgeRevealHost, "Visibility")
                .Contains("Mode=OneWay", StringComparison.Ordinal)
            && GetAttribute(commandBuilderEdgeRevealHost, "Visibility")
                .Contains("Mode=OneWay", StringComparison.Ordinal),
            "Read-only pane visibility must remain a one-way layout binding.");
        Require(
            !string.IsNullOrWhiteSpace(
                GetAttribute(explorerReopen, "AutomationProperties.Name"))
            && !string.IsNullOrWhiteSpace(
                GetAttribute(commandBuilderReopen, "AutomationProperties.Name")),
            "The universal pane reopen controls need accessible names.");
        var sharedChevronGeometry = appDocument
            .Descendants(Presentation + "Geometry")
            .SingleOrDefault(element =>
                (string?)element.Attribute(Xaml + "Key")
                == "WorkspaceDoubleChevronGeometry");
        var sharedPanelToggleStyle = appDocument
            .Descendants(Presentation + "Style")
            .SingleOrDefault(element =>
                (string?)element.Attribute(Xaml + "Key")
                == "WorkspacePanelToggleButtonStyle");
        var sharedEdgeRevealStyle = appDocument
            .Descendants(Presentation + "Style")
            .SingleOrDefault(element =>
                (string?)element.Attribute(Xaml + "Key")
                == "WorkspaceEdgeRevealButtonStyle");
        var sharedBottomEdgeRevealStyle = appDocument
            .Descendants(Presentation + "Style")
            .SingleOrDefault(element =>
                (string?)element.Attribute(Xaml + "Key")
                == "WorkspaceBottomEdgeRevealButtonStyle");
        var sharedBoundaryToggleStyle = appDocument
            .Descendants(Presentation + "Style")
            .SingleOrDefault(element =>
                (string?)element.Attribute(Xaml + "Key")
                == "WorkspaceBoundaryToggleButtonStyle");
        var sidebarToggleStyle = appDocument
            .Descendants(Presentation + "Style")
            .SingleOrDefault(element =>
                (string?)element.Attribute(Xaml + "Key")
                == "WorkspaceSidebarToggleButtonStyle");
        var sidebarSelectorStyle = appDocument
            .Descendants(Presentation + "Style")
            .Single(element =>
                (string?)element.Attribute(Xaml + "Key")
                == "CommandBuilderPageToggleStyle");
        var edgeHitTargetBrush = appDocument
            .Descendants(Presentation + "SolidColorBrush")
            .SingleOrDefault(element =>
                (string?)element.Attribute(Xaml + "Key")
                == "WorkspaceEdgeHitTargetBrush");
        var edgeHitSurface = sharedEdgeRevealStyle?
            .Descendants(Presentation + "Grid")
            .SingleOrDefault(element =>
                element.Attribute("Background")?.Value
                == "{StaticResource WorkspaceEdgeHitTargetBrush}");
        var bottomEdgeHitSurface = sharedBottomEdgeRevealStyle?
            .Descendants(Presentation + "Grid")
            .SingleOrDefault(element =>
                element.Attribute("Background")?.Value
                == "{StaticResource WorkspaceEdgeHitTargetBrush}");
        var edgeRevealRail = sharedEdgeRevealStyle?
            .Descendants(Presentation + "Border")
            .SingleOrDefault(element =>
                element.Attribute(Xaml + "Name")?.Value == "RevealRail");
        var bottomEdgeRevealRail = sharedBottomEdgeRevealStyle?
            .Descendants(Presentation + "Border")
            .SingleOrDefault(element =>
                element.Attribute(Xaml + "Name")?.Value == "RevealRail");
        var edgeHoverTrigger = sharedEdgeRevealStyle?
            .Descendants(Presentation + "Trigger")
            .SingleOrDefault(element =>
                element.Attribute("Property")?.Value == "IsMouseOver");
        var bottomEdgeHoverTrigger = sharedBottomEdgeRevealStyle?
            .Descendants(Presentation + "Trigger")
            .SingleOrDefault(element =>
                element.Attribute("Property")?.Value == "IsMouseOver");
        var studioPanelToggleStyle = studioDocument
            .Descendants(Presentation + "Style")
            .SingleOrDefault(element =>
                (string?)element.Attribute(Xaml + "Key")
                == "StudioPanelToggleButtonStyle");
        var workspacePanelButtons = new[]
        {
            explorerHeaderToggle,
            commandBuilderHeaderToggle,
            explorerReopen,
            commandBuilderReopen
        };
        Require(
            sharedChevronGeometry?.Value.Trim()
                == "M 9 2 L 2 12 L 9 22 M 20 2 L 13 12 L 20 22"
            && sharedPanelToggleStyle is not null
            && HasSetter(
                sharedPanelToggleStyle,
                "Foreground",
                "{StaticResource MutedForegroundBrush}")
            && HasSetter(
                sharedPanelToggleStyle,
                "Background",
                "Transparent")
            && HasSetter(
                sharedPanelToggleStyle,
                "BorderBrush",
                "Transparent")
            && sharedEdgeRevealStyle is not null
            && sharedEdgeRevealStyle.Attribute("BasedOn")?.Value
                == "{StaticResource WorkspacePanelToggleButtonStyle}"
            && edgeHitTargetBrush?.Attribute("Color")?.Value == "#01000000"
            && HasSetter(
                sharedEdgeRevealStyle,
                "Background",
                "{StaticResource WorkspaceEdgeHitTargetBrush}")
            && edgeHitSurface is not null
            && HasSetter(sharedEdgeRevealStyle, "BorderThickness", "0")
            && HasSetter(
                sharedEdgeRevealStyle,
                "HorizontalAlignment",
                "Stretch")
            && HasSetter(
                sharedEdgeRevealStyle,
                "VerticalAlignment",
                "Stretch")
            && edgeRevealRail is not null
            && edgeRevealRail.Attribute("Width")?.Value == "30"
            && edgeRevealRail.Attribute("Height")?.Value == "46"
            && edgeRevealRail.Attribute("Background")?.Value
                == "{StaticResource PanelRaisedBrush}"
            && edgeRevealRail.Attribute("BorderBrush")?.Value
                == "Transparent"
            && edgeRevealRail.Attribute("Opacity")?.Value == "0"
            && edgeHoverTrigger is not null
            && edgeHoverTrigger.Attribute("Value")?.Value == "True"
            && HasSetter(edgeHoverTrigger, "Opacity", "1")
            && sharedBottomEdgeRevealStyle is not null
            && sharedBottomEdgeRevealStyle.Attribute("BasedOn")?.Value
                == "{StaticResource WorkspacePanelToggleButtonStyle}"
            && HasSetter(
                sharedBottomEdgeRevealStyle,
                "Background",
                "{StaticResource WorkspaceEdgeHitTargetBrush}")
            && bottomEdgeHitSurface is not null
            && bottomEdgeRevealRail is not null
            && bottomEdgeRevealRail.Attribute("Width")?.Value == "46"
            && bottomEdgeRevealRail.Attribute("Height")?.Value == "30"
            && bottomEdgeRevealRail.Attribute("Opacity")?.Value == "0"
            && bottomEdgeHoverTrigger is not null
            && bottomEdgeHoverTrigger.Attribute("Value")?.Value == "True"
            && HasSetter(bottomEdgeHoverTrigger, "Opacity", "1")
            && sharedBoundaryToggleStyle is not null
            && HasSetter(sharedBoundaryToggleStyle, "Width", "30")
            && HasSetter(sharedBoundaryToggleStyle, "Height", "46")
            && HasSetter(sharedBoundaryToggleStyle, "Opacity", "0")
            && studioPanelToggleStyle?.Attribute("BasedOn")?.Value
                == "{StaticResource WorkspacePanelToggleButtonStyle}",
            "The shared Studio panel arrows no longer provide a hit-testable hidden-until-hover edge-reveal treatment.");
        Require(
            sidebarToggleStyle is not null
            && HasSetter(sidebarToggleStyle, "Width", "31")
            && HasSetter(sidebarToggleStyle, "Height", "31")
            && HasSetter(sidebarSelectorStyle, "Width", "31")
            && HasSetter(sidebarSelectorStyle, "Height", "31")
            && HasSetter(sidebarToggleStyle, "Margin", "4,0")
            && HasSetter(sidebarToggleStyle, "VerticalAlignment", "Center")
            && HasSetter(sidebarToggleStyle, "Background", "{StaticResource PanelRaisedBrush}")
            && HasSetter(sidebarToggleStyle, "Foreground", "{StaticResource MutedForegroundBrush}")
            && HasSetter(sidebarToggleStyle, "BorderThickness", "0")
            && sidebarToggleStyle.Descendants(Presentation + "Border")
                .Any(border => GetAttribute(border, "CornerRadius") == "7")
            && sidebarToggleStyle.Descendants(Presentation + "Trigger")
                .Any(trigger => GetAttribute(trigger, "Property") == "IsMouseOver"
                    && HasSetter(trigger, "Background", "#252D38"))
            && sidebarToggleStyle.Descendants(Presentation + "Trigger")
                .Any(trigger => GetAttribute(trigger, "Property") == "IsPressed"
                    && trigger.Descendants(Presentation + "ScaleTransform")
                        .Any(scale => GetAttribute(scale, "ScaleX") == "0.82"
                            && GetAttribute(scale, "ScaleY") == "0.82"))
            && !sidebarToggleStyle.Descendants(Presentation + "Setter")
                .Any(setter => GetAttribute(setter, "Property") == "Opacity"
                    || GetAttribute(setter, "Property") == "Visibility")
            && workspacePanelButtons.All(button =>
                GetAttribute(button, "Style") == "{StaticResource WorkspaceSidebarToggleButtonStyle}"
                && GetAttribute(button, "Visibility") is "" or "Visible"
                && !string.IsNullOrWhiteSpace(GetAttribute(button, "AutomationProperties.Name")))
            && !document.Descendants().Any(element =>
                (string?)element.Attribute(Xaml + "Name") is
                    "FileExplorerBoundaryToggle" or "CommandBuilderBoundaryToggle"),
            "Main sidebar controls must remain visible 31-pixel header buttons matching the sidebar selectors, without duplicate middle-edge controls.");
        Require(
            ReferenceEquals(explorerHeaderToggle.Parent, FindNamedElement(document, "FileExplorerHeaderGrid"))
            && ReferenceEquals(commandBuilderHeaderToggle.Parent, FindNamedElement(document, "CommandBuilderHeaderGrid"))
            && GetAttribute(explorerHeaderToggle, "Grid.Column") == "2"
            && GetAttribute(commandBuilderHeaderToggle, "Grid.Column") == "0"
            && new[]
                {
                    "FileExplorerHeaderPageColumn", "FileExplorerHeaderToggleColumn",
                    "CommandBuilderHeaderPageColumn", "CommandBuilderHeaderToggleColumn"
                }.All(name => GetAttribute(FindNamedElement(document, name), "Width") == "Auto"
                    && mainWindowCodeText.Contains($"{name}.Width = GridLength.Auto", StringComparison.Ordinal))
            && GetArrowAngle(explorerHeaderToggle) == "180"
            && GetArrowAngle(commandBuilderHeaderToggle) == "0"
            && GetArrowAngle(explorerReopen) == "180"
            && GetArrowAngle(commandBuilderReopen) == "0"
            && mainWindowCodeText.Contains("slot == WorkspacePanelSlot.Left ? 180 : 0", StringComparison.Ordinal),
            "Main sidebar arrows must occupy the inner edge of the selector row and point inward, including mirrored docking layouts.");
        Require(
            workspacePanelButtons.All(button =>
                button.Descendants(Presentation + "Path").Count() == 1
                && button.Descendants(Presentation + "Path").Single()
                    .Attribute("Data")?.Value
                == "{StaticResource WorkspaceDoubleChevronGeometry}"
                && button.Descendants(Presentation + "Path").Single()
                    .Attribute("StrokeThickness")?.Value == "1.5")
            && new[] { explorerEdgeRevealHost, commandBuilderEdgeRevealHost }.All(host =>
                host.Name == Presentation + "Border"
                && GetAttribute(host, "Width") == "39"
                && GetAttribute(host, "Height") == "38"
                && GetAttribute(host, "VerticalAlignment") == "Top"
                && GetAttribute(host, "ClipToBounds") == "True"
                && GetAttribute(host, "Panel.ZIndex") == "10"
                && GetAttribute(host, "Visibility").Contains("BooleanToVisibilityConverter", StringComparison.Ordinal))
            && GetAttribute(explorerEdgeRevealHost, "HorizontalAlignment") == "Left"
            && GetAttribute(commandBuilderEdgeRevealHost, "HorizontalAlignment") == "Right"
            && new[] { explorerReopen, commandBuilderReopen }.All(button =>
                GetAttribute(button, "Width") is "" or "31"
                && GetAttribute(button, "Height") is "" or "31"
                && GetAttribute(button, "Margin") == "4,0")
            && GetAttribute(workspaceTabs, "BorderThickness") == "0"
            && !mainWindowCodeText.Contains("WorkspacePopupAnchor", StringComparison.Ordinal)
            && !mainWindowCodeText.Contains("ConfigureWorkspaceEdgeRevealPopups", StringComparison.Ordinal)
            && !mainWindowCodeText.Contains("RefreshWorkspaceEdgeRevealPopups", StringComparison.Ordinal)
            && !explorerReopen.ToString(SaveOptions.DisableFormatting)
                .Contains("DropShadowEffect", StringComparison.Ordinal)
            && !commandBuilderReopen.ToString(SaveOptions.DisableFormatting)
                .Contains("DropShadowEffect", StringComparison.Ordinal),
            "Collapsed main sidebars must use compact in-window reopen buttons aligned with the 38-pixel selector row, with no floating popup or screen-coordinate correction.");
        var tabHeaderSpacing = workspaceTabs.Descendants(Presentation + "Grid.Style")
            .Select(element => element.Element(Presentation + "Style"))
            .SingleOrDefault(style => style?.Descendants(Presentation + "DataTrigger")
                .Any(trigger => GetAttribute(trigger, "Binding") == "{Binding ShouldShowFileExplorerReopenButton}") == true);
        Require(
            tabHeaderSpacing is not null
            && HasSetter(tabHeaderSpacing, "Margin", "0")
            && tabHeaderSpacing.Descendants(Presentation + "DataTrigger").Any(trigger =>
                GetAttribute(trigger, "Binding") == "{Binding ShouldShowFileExplorerReopenButton}"
                && GetAttribute(trigger, "Value") == "True"
                && HasSetter(trigger, "Margin", "39,0,0,0"))
            && tabHeaderSpacing.Descendants(Presentation + "DataTrigger").Any(trigger =>
                GetAttribute(trigger, "Binding") == "{Binding ShouldShowCommandBuilderReopenButton}"
                && GetAttribute(trigger, "Value") == "True"
                && HasSetter(trigger, "Margin", "0,0,39,0"))
            && tabHeaderSpacing.Descendants(Presentation + "MultiDataTrigger").Any(trigger =>
                HasSetter(trigger, "Margin", "39,0,39,0")
                && new[] { "ShouldShowFileExplorerReopenButton", "ShouldShowCommandBuilderReopenButton" }
                    .All(property => trigger.Descendants(Presentation + "Condition").Any(condition =>
                        GetAttribute(condition, "Binding") == $"{{Binding {property}}}"
                        && GetAttribute(condition, "Value") == "True")))
            && GetAttribute(workspaceTabs, "Margin") == string.Empty,
            "The workspace tab header must reserve space for each visible reopen control without shrinking the document surface.");
        var mainWindowViewModelText = File.ReadAllText(FindRepositoryPath(
            "src",
            "Hypershell",
            "ViewModels",
            "MainWindowViewModel.cs"));
        Require(
            mainWindowViewModelText.Split(
                "() => !IsVisualPageStudioActive",
                StringSplitOptions.None).Length >= 3
            && mainWindowViewModelText.Split(
                "if (value && IsVisualPageStudioActive)",
                StringSplitOptions.None).Length >= 3
            && mainWindowViewModelText.Contains(
                "ShouldShowFileExplorerReopenButton",
                StringComparison.Ordinal)
            && mainWindowViewModelText.Contains(
                "ShouldShowCommandBuilderReopenButton",
                StringComparison.Ordinal)
            && mainWindowViewModelText.Contains(
                "private void SelectWorkspaceSideTool(string toolId)",
                StringComparison.Ordinal)
            && mainWindowViewModelText.Contains(
                "if (IsVisualPageStudioActive)",
                StringComparison.Ordinal),
            "Hyperpage Studio no longer blocks the normal Hypershell side panels and their reopen controls.");
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
            && GetAttribute(accountMenu, "ToolTip") == "Account status"
            && GetAttribute(accountMenu, "AutomationProperties.HelpText")
                .Contains("ProfileSyncTitleToolTip", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(
                GetAttribute(accountMenu, "AutomationProperties.Name")),
            "The account dropdown lost its click handler, live session status, or accessible name.");
        Require(
            GetAttribute(signInItem, "Header") == "Login"
            && GetAttribute(signInItem, "Click")
                == "ShowProfileLoginGateClick"
            && GetAttribute(signInItem, "IsEnabled")
                .Contains("CanProfileSyncSignIn", StringComparison.Ordinal),
            "The signed-out account menu no longer opens the dedicated login gate.");
        Require(
            GetAttribute(signOutItem, "Header") == "Logout"
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
                      "Header=\"Account\"",
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
                     "Content=\"Login\"",
                     "Content=\"Offline\"",
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
                "Text=\"MODEL\"",
                StringComparison.Ordinal)
            && mainWindowText.Contains(
                "Text=\"PROJECT\"",
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
            settingsMarkup.Contains("Text=\"Account\"", StringComparison.Ordinal)
            && settingsMarkup.Contains("Text=\"Devices\"", StringComparison.Ordinal)
            && !settingsMarkup.Contains("Account &amp; Sync", StringComparison.Ordinal)
            && !settingsMarkup.Contains("Account Sync", StringComparison.Ordinal)
            && !settingsMarkup.Contains("Remote devices (SSH)", StringComparison.Ordinal)
            && !settingsMarkup.Contains("What follows you", StringComparison.Ordinal)
            && !settingsMarkup.Contains("Portable Profiles", StringComparison.Ordinal)
            && !settingsMarkup.Contains("Export .hyperprofile", StringComparison.Ordinal)
            && !settingsMarkup.Contains("Remote profile revisions", StringComparison.Ordinal)
            && !settingsMarkup.Contains("Tombstone remote", StringComparison.Ordinal)
            && !settingsMarkup.Contains("Conflict-copy directory", StringComparison.Ordinal),
            "Settings must expose a compact automatic account model without verbose, portable-profile, or revision-management panels.");
        Require(
            settingsMarkup.Contains(
                "{Binding WorkspaceAiAutonomyContextName, Mode=OneWay}",
                StringComparison.Ordinal),
            "Settings must display its read-only autonomy context with a one-way binding.");
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
        Require(
            mainWindowCode.Contains(
                "WindowsMessageGetMinMaxInfo",
                StringComparison.Ordinal)
            && mainWindowCode.Contains(
                "ApplyMaximizedWorkArea",
                StringComparison.Ordinal)
            && mainWindowCode.Contains(
                "monitorInfo.WorkArea.Bottom",
                StringComparison.Ordinal)
            && mainWindowCode.Contains(
                "minMaxInfo.MaxTrackSize = minMaxInfo.MaxSize",
                StringComparison.Ordinal),
            "The custom-chrome window must maximize to the monitor work area instead of covering the Windows taskbar.");
        var linkedHostsCard = FindNamedElement(
            settingsDocument,
            "LinkedHostsSettingsCard");
        Require(
            linkedHostsCard.ToString(SaveOptions.DisableFormatting)
                .Contains("Verified devices", StringComparison.Ordinal)
            && settingsDocument
                .Descendants()
                .Any(element =>
                    (string?)element.Attribute(Xaml + "Name")
                    == "LinkedHostDisplayNameBox"),
            "The linked-host settings destination must retain concise device guidance and a focus target.");

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
                     "AiShellGlyph",
                     "ShellCore",
                     "PulseHalo",
                     "SecondaryVividBrush",
                     "IsKeyboardFocused",
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
            "WORKSPACE_CHROME_OK|minimal-hyperdesk-login|startup-login-gate|settings-binding-safe|ai-composer-recovery|account-login-dropdown|linked-host-device-entry|visible-sidebar-header-toggles|compact-sidebar-reopen|shared-hyperpage-panel-arrows|hit-testable-dpi-safe-edge-reveal|studio-side-panel-lock|taskbar-safe-maximize|3-shell-live-toggles|active-pulse");
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

    private static string GetArrowAngle(XElement button) =>
        (string?)button.Descendants(Presentation + "RotateTransform")
            .SingleOrDefault()?.Attribute("Angle") ?? "0";

    private static bool HasSetter(
        XElement style,
        string property,
        string value) => style.Elements(Presentation + "Setter").Any(element =>
            element.Attribute("Property")?.Value == property
            && element.Attribute("Value")?.Value == value);

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
