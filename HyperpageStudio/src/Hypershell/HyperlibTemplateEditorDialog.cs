using System.Globalization;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using HyperLib.Core;
using Hypershell.Services;

namespace Hypershell;

/// <summary>Edits reusable elements and their authoring contract without changing content pages.</summary>
public sealed class HyperlibTemplateEditorDialog : Window
{
    private readonly string _root;
    private VisualPageUserSourceSnapshot _snapshot;
    private HyperlibTemplateEditSession _session;
    private readonly string? _libraryRoot;
    private readonly List<TemplateImportPlan> _importPlans = [];
    private readonly Action<string>? _openSource;
    private readonly bool _reusableTemplate;
    private readonly Dictionary<(object Owner, string Key), string> _errors = [];
    private readonly Dictionary<(object Owner, string Key), string> _textDrafts = [];
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 20, 0) };
    private readonly Button _save = new() { Content = "_Save template", MinWidth = 120, Padding = new(16, 9, 16, 9) };
    private Button? _saveDesignGroup;
    private readonly TabControl _tabs = new() { Margin = new(0, 12, 0, 0) };
    private TabControl? _advancedTabs;
    private bool _dirty, _saving, _ready, _closed;
    private ListBox _elements = null!, _defaults = null!, _regions = null!, _styles = null!;
    private ContentControl _elementDetails = null!, _defaultDetails = null!, _regionDetails = null!, _styleDetails = null!;

    public HyperlibTemplateEditorDialog(string root, TemplateDefinition definition,
        VisualPageUserSourceSnapshot snapshot, Action<string>? openSource = null, bool reusableTemplate = false, string? libraryRoot = null)
    {
        _root = root; _snapshot = snapshot; _session = new(definition); _openSource = openSource; _reusableTemplate = reusableTemplate;
        _libraryRoot = libraryRoot ?? HyperlibTemplateObjectAuthoringService.FindLibraryRoot(root);
        Title = reusableTemplate ? "Template elements · " + definition.Title : "Work structure · " + definition.Title; Width = 1100; Height = 800; MinWidth = 850; MinHeight = 650;
        _save.Content = reusableTemplate ? "_Save Template" : "_Save Work structure";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme("AppBackgroundBrush", "#12161C"); Foreground = Theme("ForegroundBrush", "#E6EAF0");
        ConfigureTabs(_tabs);
        var outer = new DockPanel { Margin = new(22) };
        var header = new StackPanel();
        header.Children.Add(Heading(definition.Title, 24));
        header.Children.Add(Help(reusableTemplate
            ? "Collect reusable elements, page layouts, and shared styles from Design Groups. Define the fields authors can fill, then use your Template in a Work."
            : "Define the layout new Pages begin with: headers, sections, decoration, and element locations. Add Work-specific elements here; set shared colors and typography in the Work workspace."));
        var libraryActions = new WrapPanel();
        var browseLibrary = Button("Browse Core Library", (_, _) => BrowseLibrary(), "Browse Design Groups and add reusable objects to this draft");
        browseLibrary.IsEnabled = _libraryRoot is not null; libraryActions.Children.Add(browseLibrary);
        if (reusableTemplate)
        {
            _saveDesignGroup = Button("Save as Design Group", SaveDesignGroupClick, "Save this Template as an independent Design Group in the Core Library");
            libraryActions.Children.Add(_saveDesignGroup);
        }
        header.Children.Add(libraryActions);
        DockPanel.SetDock(header, Dock.Top); outer.Children.Add(header);
        var footer = new DockPanel { Margin = new(0, 16, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Button("Close", (_, _) => Close())); _save.Click += SaveClick; actions.Children.Add(_save);
        DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions); footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom); outer.Children.Add(footer);
        var elements = new TabItem { Header = reusableTemplate ? "Elements" : "Special elements", Tag = "elements", Content = ElementsView() };
        var layout = new TabItem { Header = "Page layout", Tag = "defaults", Content = DefaultsView() };
        var structure = new TabItem { Header = "Element locations", Tag = "regions", Content = RegionsView() };
        var styles = new TabItem { Header = "Appearance contract", Tag = "styles", Content = StylesView() };
        _advancedTabs = new TabControl { Margin = new(12) };
        ConfigureTabs(_advancedTabs);
        if (reusableTemplate)
        {
            _tabs.Items.Add(elements);
            _advancedTabs.Items.Add(layout);
            _advancedTabs.Items.Add(structure);
        }
        else
        {
            _tabs.Items.Add(layout);
            _tabs.Items.Add(structure);
            _tabs.Items.Add(elements);
        }
        _advancedTabs.Items.Add(styles);
        foreach (var tab in _advancedTabs.Items.OfType<TabItem>()) tab.Padding = new Thickness(12, 8, 12, 8);
        _tabs.Items.Add(new TabItem { Header = "Advanced", Tag = "advanced", Content = _advancedTabs });
        foreach (var tab in _tabs.Items.OfType<TabItem>()) tab.Padding = new Thickness(12, 8, 12, 8);
        _tabs.SelectedIndex = 0;
        outer.Children.Add(_tabs); Content = outer;
        Closing += (_, args) =>
        {
            if (_saving) { args.Cancel = true; return; }
            if (_dirty && MessageBox.Show(this, reusableTemplate ? "Discard unsaved Template changes?" : "Discard unsaved Work structure changes?", Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                args.Cancel = true;
        };
        Closed += (_, _) => _closed = true;
        _ready = true; RefreshLists(); Validate();
    }

    public void SelectSection(string section)
    {
        if (section.Equals("library", StringComparison.OrdinalIgnoreCase))
        {
            if (IsLoaded) BrowseLibrary();
            else Loaded += OpenLibraryOnLoaded;
            return;
        }
        section = section.ToLowerInvariant() switch { "layout" or "structure" => "defaults", "locations" => "regions", "fields" => "elements", _ => section };
        if (_tabs.Items.OfType<TabItem>().FirstOrDefault(tab => string.Equals(tab.Tag as string, section, StringComparison.OrdinalIgnoreCase)) is { } selected)
            _tabs.SelectedItem = selected;
        else if (_advancedTabs?.Items.OfType<TabItem>().FirstOrDefault(tab => string.Equals(tab.Tag as string, section, StringComparison.OrdinalIgnoreCase)) is { } advanced)
        {
            _advancedTabs.SelectedItem = advanced;
            _tabs.SelectedItem = _tabs.Items.OfType<TabItem>().First(tab => tab.Tag as string == "advanced");
        }
    }

    private void OpenLibraryOnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OpenLibraryOnLoaded;
        BrowseLibrary();
    }

    private void BrowseLibrary()
    {
        if (_saving || _closed || _libraryRoot is null) return;
        if (_errors.Count > 0) { _status.Text = "Finish the highlighted field before adding objects."; return; }
        try
        {
            _session.Validate();
            var browser = new HyperlibObjectLibraryDialog(_libraryRoot, _root, _session.CreateDefinition(), _importPlans) { Owner = this };
            if (browser.ShowDialog() != true || browser.ImportPlan is not { } plan) return;
            AddLibraryObjects(plan);
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    internal void AddLibraryObjects(TemplateImportPlan plan)
    {
        _session = new(plan.Definition); _importPlans.Add(plan); _textDrafts.Clear(); _errors.Clear();
        RefreshLists(); Changed();
            _status.Text = plan.Summary + (_reusableTemplate ? " Save to add this collection to the Template." : " Save to add this collection to the Work.");
    }

    private async void SaveDesignGroupClick(object sender, RoutedEventArgs args)
    {
        if (_dirty || _saving || _libraryRoot is null) return;
        var dialog = new Window { Owner = this, Title = "Save Design Group", Width = 470, MinWidth = 400, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background, Foreground = Foreground };
        var panel = new StackPanel { Margin = new(24) };
        panel.Children.Add(Heading("Save as Design Group", 22));
        panel.Children.Add(Help("Publish the saved elements, layouts, styles, and their component source as an independent collection in the Core Library."));
        var name = new TextBox { Text = _session.Draft.Title, Padding = new(10), Margin = new(0, 8, 0, 16) };
        AutomationProperties.SetName(name, "Design Group name"); panel.Children.Add(name);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Button("Cancel", (_, _) => dialog.Close()));
        var create = Button("Save Design Group", (_, _) => { if (!string.IsNullOrWhiteSpace(name.Text)) dialog.DialogResult = true; });
        create.IsDefault = true; buttons.Children.Add(create); panel.Children.Add(buttons); dialog.Content = panel;
        if (dialog.ShowDialog() != true) return;
        _saving = true; IsEnabled = false;
        try
        {
            var current = await new VisualPageUserSourceService().LoadAsync(_root, _snapshot.RelativePath);
            if (current.Sha256 != _snapshot.Sha256) throw new InvalidOperationException("This Template changed outside the editor. Reopen it before saving a Design Group.");
            var group = await TemplateObjectLibrary.SaveDesignGroupAsync(_libraryRoot, _root, name.Text.Trim());
            _status.Text = "“" + group.Title + "” is now available in the Core Library.";
        }
        catch (Exception error) { _status.Text = error.Message; }
        finally { _saving = false; IsEnabled = true; }
    }

    private static void ConfigureTabs(TabControl tabs)
    {
        // The application-wide tab template includes a terminal-add action.
        var dock = new FrameworkElementFactory(typeof(DockPanel));
        var headers = new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.TabPanel));
        headers.SetValue(DockPanel.DockProperty, Dock.Top);
        headers.SetValue(Panel.IsItemsHostProperty, true);
        headers.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 8));
        dock.AppendChild(headers);
        var content = new FrameworkElementFactory(typeof(ContentPresenter), "PART_SelectedContentHost");
        content.SetValue(ContentPresenter.ContentSourceProperty, "SelectedContent");
        dock.AppendChild(content);
        tabs.Template = new ControlTemplate(typeof(TabControl)) { VisualTree = dock };
    }

    private FrameworkElement ElementsView()
    {
        var left = new DockPanel(); var toolbar = new StackPanel(); toolbar.Children.Add(Heading(_reusableTemplate ? "Reusable element library" : "Work-specific elements"));
        toolbar.Children.Add(Help(_reusableTemplate
            ? "Define reusable elements and the text, media, or other fields their authors can fill."
            : "Elements copied from this Work's Template, plus any additions this Work needs."));
        var preset = ChoiceBox(HyperlibStudioAuthoringService.GetElementPresets(_root, _session.Draft), "Element renderer preset", nameof(HyperlibElementPreset.Title));
        toolbar.Children.Add(preset);
        toolbar.Children.Add(Button("Add element", (_, _) => Act(() =>
        {
            if (preset.SelectedItem is HyperlibElementPreset selected) RefreshLists(elementId: _session.AddElement(selected.Definition).Id);
        })));
        DockPanel.SetDock(toolbar, Dock.Top); left.Children.Add(toolbar);
        _elements = List("Template elements"); left.Children.Add(_elements); _elementDetails = new ContentControl();
        _elements.SelectionChanged += (_, _) => ShowElement();
        return Split(left, _elementDetails);
    }

    private void ShowElement()
    {
        var block = _session.Draft.Blocks.FirstOrDefault(item => item.Id == SelectedId(_elements));
        if (block is null) { _elementDetails.Content = Help("Add an element using a renderer already present in this project."); return; }
        var panel = new DockPanel(); var summary = new StackPanel();
        summary.Children.Add(EditText(block, "title", "Element name", block.Title, value =>
        {
            block.Title = value; UpdateLabel(_elements, block.Id, value);
            foreach (var (instance, index) in _session.Draft.DefaultBlocks.Select((item, index) => (item, index)).Where(pair => pair.item.Type == block.Id))
                UpdateLabel(_defaults, instance.Id, $"{index + 1}. {value}");
        }));
        var commands = new WrapPanel();
        commands.Children.Add(Button("Duplicate", (_, _) => Act(() => RefreshLists(elementId: _session.DuplicateElement(block.Id).Id))));
        commands.Children.Add(Button("Remove", (_, _) => Act(() => { _session.RemoveElement(block.Id); RefreshLists(); })));
        var path = HyperlibStudioAuthoringService.FindComponentSource(_root, block.Component);
        var source = Button("Open component", (_, _) =>
        {
            if (path is null || _openSource is null) return;
            Close();
            if (_closed) _openSource(path);
        });
        source.IsEnabled = path is not null && _openSource is not null; source.ToolTip = path ?? "Component source is not available in this project.";
        summary.Children.Add(commands);
        var implementation = new StackPanel();
        implementation.Children.Add(Help($"Stable ID: {block.Id} · {block.Kind} · {block.Component}"));
        implementation.Children.Add(source);
        implementation.Children.Add(Help("The component supplies behavior. Duplicate reuses it with an independent field contract."));
        if (_reusableTemplate)
        {
            summary.Children.Add(new Expander { Header = "Component source", Content = implementation, Margin = new(0, 12, 0, 12) });
            DockPanel.SetDock(summary, Dock.Top); panel.Children.Add(summary);
            panel.Children.Add(FieldsView(block.Fields));
        }
        else
        {
            var advanced = new DockPanel();
            DockPanel.SetDock(implementation, Dock.Top); advanced.Children.Add(implementation); advanced.Children.Add(FieldsView(block.Fields));
            DockPanel.SetDock(summary, Dock.Top); panel.Children.Add(summary);
            panel.Children.Add(new Expander { Header = "Advanced element definition", Content = advanced, Margin = new(0, 12, 0, 0) });
        }
        _elementDetails.Content = panel;
    }

    private FrameworkElement FieldsView(List<FieldDefinition> fields)
    {
        var left = new DockPanel(); var toolbar = new StackPanel(); toolbar.Children.Add(Heading("Field definitions", 15));
        var list = List("Element fields"); var detail = new ContentControl();
        void Refresh(string? id = null) => SetChoices(list, fields.Select(field => new Choice(field.Id, field.Title)), id);
        var add = new WrapPanel();
        add.Children.Add(Button("Style +", (sender, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = sender as Button };
            foreach (var preset in HyperlibTemplateEditSession.SupportedElementStylePresets())
            {
                var item = new MenuItem { Header = preset.Title };
                item.Click += (_, _) => Act(() => { HyperlibTemplateEditSession.AddSupportedField(fields, preset); Refresh(preset.Id); });
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }, "Expose a supported element styling control"));
        add.Children.Add(Button("Field +", (_, _) => Act(() => Refresh(HyperlibTemplateEditSession.AddCustomField(fields).Id)), "Add a custom field that requires component binding"));
        toolbar.Children.Add(add);
        DockPanel.SetDock(toolbar, Dock.Top); left.Children.Add(toolbar); left.Children.Add(list);
        list.SelectionChanged += (_, _) =>
        {
            var field = fields.FirstOrDefault(item => item.Id == SelectedId(list));
            detail.Content = field is null ? Help("Select a field to edit its label, type, default, and available values.")
                : FieldEditor(field, () => { fields.Remove(field); Refresh(); Changed(); }, false, () => UpdateLabel(list, field.Id, field.Title));
        };
        Refresh(); return Split(left, detail, 170);
    }

    private FrameworkElement FieldEditor(FieldDefinition field, Action remove, bool appearance, Action? refreshLabel = null)
    {
        var panel = new StackPanel();
        panel.Children.Add(EditText(field, "title", "Label", field.Title, value => { field.Title = value; refreshLabel?.Invoke(); }));
        panel.Children.Add(Help("Stable field ID: " + field.Id));
        var type = ChoiceBox(new[] { "string", "text", "number", "boolean", "color", "media", "url", "select" }, "Field type");
        type.SelectedItem = field.Type;
        type.SelectionChanged += (_, _) => { if (type.SelectedItem is string value) { field.Type = value; Changed(); } };
        panel.Children.Add(Labelled("Type", type));
        var required = new CheckBox { Content = "Required", IsChecked = field.Required, Margin = new(0, 10, 0, 10) };
        required.Click += (_, _) => { field.Required = required.IsChecked == true; Changed(); }; panel.Children.Add(required);
        var mode = ChoiceBox(new[] { "Inherit / no default", "Default value", "Explicit null" }, "Default mode");
        mode.SelectedIndex = (int)HyperlibTemplateEditSession.DefaultMode(field);
        var value = new TextBox { Text = TextDraft(field, "default", HyperlibTemplateEditSession.DefaultText(field)), MinWidth = 60, Padding = new(8), AcceptsReturn = true, MinHeight = 38, MaxHeight = 120, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetName(value, "Default value");
        void ApplyDefault()
        {
            _textDrafts[(field, "default")] = value.Text;
            TryEdit(field, "default", () => HyperlibTemplateEditSession.SetDefault(field, (HyperlibDefaultMode)mode.SelectedIndex, value.Text));
            value.IsEnabled = mode.SelectedIndex == (int)HyperlibDefaultMode.Value;
        }
        mode.SelectionChanged += (_, _) => ApplyDefault(); value.TextChanged += (_, _) => ApplyDefault();
        value.IsEnabled = mode.SelectedIndex == (int)HyperlibDefaultMode.Value;
        panel.Children.Add(Labelled("Default", mode)); panel.Children.Add(value);
        if (field.Type == "color")
        {
            var choose = ColorButton(field.Title, () => value.Text, selected =>
            {
                mode.SelectedIndex = (int)HyperlibDefaultMode.Value;
                value.Text = selected;
            });
            panel.Children.Add(choose);
        }
        ComboBox? defaultChoices = null;
        var refreshingChoices = false;
        if (field.Type is "select" or "boolean")
        {
            var choices = ChoiceBox(field.Type == "select" ? field.Options : new List<string> { "true", "false" }, "Choose default value");
            defaultChoices = choices;
            choices.SelectedItem = value.Text;
            choices.SelectionChanged += (_, _) =>
            {
                if (!refreshingChoices && choices.SelectedItem is string selected)
                {
                    mode.SelectedIndex = (int)HyperlibDefaultMode.Value;
                    value.Text = selected;
                }
            };
            panel.Children.Add(choices);
        }
        panel.Children.Add(Help("No default preserves inheritance. A blank text default is an intentional empty string."));
        var bounds = new Grid(); bounds.ColumnDefinitions.Add(new()); bounds.ColumnDefinitions.Add(new());
        var min = EditText(field, "min", "Minimum", field.Min?.ToString(CultureInfo.InvariantCulture) ?? "", text => field.Min = OptionalNumber(text));
        var max = EditText(field, "max", "Maximum", field.Max?.ToString(CultureInfo.InvariantCulture) ?? "", text => field.Max = OptionalNumber(text));
        min.Margin = new(0, 0, 8, 0); Grid.SetColumn(max, 1); bounds.Children.Add(min); bounds.Children.Add(max); panel.Children.Add(bounds);
        bounds.Visibility = field.Type == "number" ? Visibility.Visible : Visibility.Collapsed;
        var optionGroup = new StackPanel { Visibility = field.Type == "select" ? Visibility.Visible : Visibility.Collapsed };
        optionGroup.Children.Add(Heading("Options", 14)); optionGroup.Children.Add(Help("Each row is one exact value. Commas and empty strings are preserved."));
        var options = new StackPanel();
        void RefreshDefaultChoices()
        {
            refreshingChoices = true;
            try { defaultChoices?.Items.Refresh(); }
            finally { refreshingChoices = false; }
        }
        void RefreshOptions()
        {
            RefreshDefaultChoices();
            options.Children.Clear();
            for (var index = 0; index < field.Options.Count; index++)
            {
                var position = index; var row = new DockPanel { Margin = new(0, 0, 0, 6) };
                var delete = Button("−", (_, _) => { field.Options.RemoveAt(position); RefreshOptions(); Changed(); }, "Remove option " + (position + 1));
                DockPanel.SetDock(delete, Dock.Right); row.Children.Add(delete);
                var text = new TextBox { Text = field.Options[position], Padding = new(7), MinWidth = 70 };
                AutomationProperties.SetName(text, "Option " + (position + 1));
                text.TextChanged += (_, _) => { field.Options[position] = text.Text; RefreshDefaultChoices(); Changed(); };
                row.Children.Add(text); options.Children.Add(row);
            }
        }
        RefreshOptions(); optionGroup.Children.Add(options);
        optionGroup.Children.Add(Button("Add option", (_, _) => { field.Options.Add(""); RefreshOptions(); Changed(); }));
        panel.Children.Add(optionGroup);
        type.SelectionChanged += (_, _) =>
        {
            bounds.Visibility = field.Type == "number" ? Visibility.Visible : Visibility.Collapsed;
            optionGroup.Visibility = field.Type == "select" ? Visibility.Visible : Visibility.Collapsed;
        };
        var supported = appearance ? HyperlibTemplateEditSession.SupportedAppearancePresets().Any(item => item.Id == field.Id)
            : HyperlibTemplateEditSession.IsSupportedElementStyle(field.Id);
        panel.Children.Add(Help(supported ? "Supported styling control: the HyperLib renderer consumes this field."
            : appearance ? "Custom styling requires an explicit renderer binding. Adding a field does not create a CSS rule."
            : "Content and custom fields must match the component's field binding. Open the component to change its behavior."));
        panel.Children.Add(Button("Remove field", (_, _) => remove())); return Scroll(panel);
    }

    private FrameworkElement StylesView()
    {
        var wrapper = new DockPanel();
        var help = Help("Define the appearance fields this contract supports. Set their actual colors, fonts, and text size in a Work. In-use controls cannot be removed.");
        DockPanel.SetDock(help, Dock.Top); wrapper.Children.Add(help);
        var left = new DockPanel(); var top = new StackPanel(); top.Children.Add(Heading("Supported styles"));
        var preset = ChoiceBox(HyperlibTemplateEditSession.SupportedAppearancePresets(), "Page styling preset", nameof(FieldDefinition.Title)); top.Children.Add(preset);
        top.Children.Add(Button("Expose style", (_, _) => Act(() =>
        {
            if (preset.SelectedItem is FieldDefinition selected) { HyperlibTemplateEditSession.AddSupportedField(_session.Draft.Appearance, selected); RefreshLists(styleId: selected.Id); }
        })));
        top.Children.Add(Button("Add custom control", (_, _) => Act(() => RefreshLists(styleId: HyperlibTemplateEditSession.AddCustomField(_session.Draft.Appearance).Id))));
        DockPanel.SetDock(top, Dock.Top); left.Children.Add(top);
        _styles = List("Page styling controls"); left.Children.Add(_styles); _styleDetails = new ContentControl();
        _styles.SelectionChanged += (_, _) =>
        {
            var field = _session.Draft.Appearance.FirstOrDefault(item => item.Id == SelectedId(_styles));
            _styleDetails.Content = field is null ? Help("Expose a supported styling control to make it available in HyperLib.")
                : FieldEditor(field, () => { _session.Draft.Appearance.Remove(field); RefreshLists(); Changed(); }, true, () => UpdateLabel(_styles, field.Id, field.Title));
        };
        wrapper.Children.Add(Split(left, _styleDetails)); return wrapper;
    }

    private FrameworkElement DefaultsView()
    {
        var left = new DockPanel(); var toolbar = new StackPanel(); toolbar.Children.Add(Heading("Page layout"));
        toolbar.Children.Add(Help(_reusableTemplate
            ? "Existing Template layout presets stay available here for compatibility. Set a Work's Page structure and element locations in that Work."
            : "Arrange the headers, sections, decoration, and other elements new Pages begin with. Choose their locations and layout widths. Existing Pages keep their own arrangement."));
        toolbar.Children.Add(Button("Add element", (_, _) => ShowAddDefault()));
        var commands = new WrapPanel();
        commands.Children.Add(Button("↑", (_, _) => MoveDefault(-1), "Move selected default element up"));
        commands.Children.Add(Button("↓", (_, _) => MoveDefault(1), "Move selected default element down"));
        commands.Children.Add(Button("Remove", (_, _) => Act(() => { if (SelectedId(_defaults) is { } id) _session.RemoveDefaultBlock(id); RefreshLists(); })));
        toolbar.Children.Add(commands); DockPanel.SetDock(toolbar, Dock.Top); left.Children.Add(toolbar);
        _defaults = List("New page elements in order"); left.Children.Add(_defaults); _defaultDetails = new ContentControl();
        _defaults.SelectionChanged += (_, _) => ShowDefault(); return Split(left, _defaultDetails, 245);
    }

    private void ShowAddDefault()
    {
        var panel = new StackPanel(); panel.Children.Add(Heading("Add initial element"));
        var block = ChoiceBox(_session.Draft.Blocks, "Initial element type", nameof(BlockDefinition.Title));
        var region = ChoiceBox(_session.Draft.Regions, "Initial element region", nameof(TemplateRegion.Title));
        panel.Children.Add(Labelled("Element", block)); panel.Children.Add(Labelled("Region", region));
        panel.Children.Add(Button("Add to new pages", (_, _) => Act(() =>
        {
            if (block.SelectedItem is BlockDefinition schema && region.SelectedItem is TemplateRegion target)
                RefreshLists(defaultId: _session.AddDefaultBlock(schema.Id, target.Id).Id);
        })));
        _defaultDetails.Content = panel;
    }

    private void MoveDefault(int direction) => Act(() =>
    {
        var id = SelectedId(_defaults); if (id is null) return;
        _session.MoveDefaultBlock(id, direction); RefreshLists(defaultId: id);
    });

    private void ShowDefault()
    {
        var block = _session.Draft.DefaultBlocks.FirstOrDefault(item => item.Id == SelectedId(_defaults));
        if (block is null) { _defaultDetails.Content = Help("No initial layout is declared. HyperLib uses the first allowed element when creating a page. Add elements here to define its starting layout."); return; }
        var schema = _session.Draft.Blocks.FirstOrDefault(item => item.Id == block.Type);
        if (schema is null) { _defaultDetails.Content = Help("This default references an element that is not defined."); return; }
        var panel = new StackPanel(); var heading = Heading(schema.Title); heading.ToolTip = "Element instance: " + block.Id; panel.Children.Add(heading);
        var region = ChoiceBox(_session.Draft.Regions, "Default element region", nameof(TemplateRegion.Title));
        region.SelectedItem = _session.Draft.Regions.FirstOrDefault(item => item.Id == block.Region);
        region.SelectionChanged += (_, _) => { if (region.SelectedItem is TemplateRegion target) { block.Region = target.Id; Changed(); } };
        panel.Children.Add(Labelled("Place in", region));
        foreach (var field in schema.Fields)
        {
            var section = new StackPanel { Margin = new(0, 7, 0, 7) };
            var use = new CheckBox { Content = field.Title, IsChecked = block.Values.ContainsKey(field.Id), ToolTip = "Use a specific value for this layout element" };
            var value = block.Values.TryGetValue(field.Id, out var stored) ? stored : HyperlibTemplateEditSession.InitialValue(field);
            var input = ValueInput(block, field, value); input.IsEnabled = use.IsChecked == true;
            use.Click += (_, _) => TryEdit(block, field.Id, () =>
            {
                if (use.IsChecked == true) block.Values[field.Id] = HyperlibTemplateEditSession.InitialValue(field);
                else { block.Values.Remove(field.Id); _textDrafts.Remove((block, field.Id)); }
                ShowDefault();
            });
            section.Children.Add(use); section.Children.Add(input); panel.Children.Add(section);
        }
        foreach (var key in block.Values.Keys.Where(key => schema.Fields.All(field => field.Id != key)).ToList())
        {
            panel.Children.Add(Help($"Stored override '{key}' has no exposed field. Restore the field or remove this default override."));
            panel.Children.Add(Button("Remove " + key + " override", (_, _) =>
            {
                block.Values.Remove(key); _errors.Remove((block, key)); _textDrafts.Remove((block, key)); ShowDefault(); Changed();
            }));
        }
        _defaultDetails.Content = Scroll(panel);
    }

    private FrameworkElement ValueInput(PageBlock block, FieldDefinition field, JsonElement value)
    {
        if (field.Type == "select")
        {
            var choices = ChoiceBox(field.Options, field.Title); choices.SelectedItem = HyperlibTemplateEditSession.ValueText(value);
            choices.SelectionChanged += (_, _) => { if (choices.SelectedItem is string selected) { block.Values[field.Id] = ContentJson.Value(selected); Changed(); } }; return choices;
        }
        if (field.Type == "boolean")
        {
            var check = new CheckBox { Content = field.Title, IsChecked = value.ValueKind == JsonValueKind.True, Margin = new(0, 8, 0, 8) };
            check.Click += (_, _) => { block.Values[field.Id] = ContentJson.Value(check.IsChecked == true); Changed(); }; return check;
        }
        var input = EditText(block, field.Id, field.Type == "color" ? "Hex color" : field.Title,
            HyperlibTemplateEditSession.ValueText(value), text => block.Values[field.Id] = HyperlibTemplateEditSession.ParseValue(field.Type, text), field.Type == "text");
        if (field.Type != "color") return input;
        var panel = new StackPanel(); panel.Children.Add(input);
        panel.Children.Add(ColorButton(field.Title, () => block.Values.TryGetValue(field.Id, out var current) ? HyperlibTemplateEditSession.ValueText(current) : "", selected =>
        {
            block.Values[field.Id] = ContentJson.Value(selected);
            _textDrafts.Remove((block, field.Id)); _errors.Remove((block, field.Id));
            ShowDefault(); Changed();
        }));
        return panel;
    }

    private FrameworkElement RegionsView()
    {
        var left = new DockPanel(); var toolbar = new StackPanel(); toolbar.Children.Add(Heading("Element locations"));
        toolbar.Children.Add(Help(_reusableTemplate
            ? "Existing Template regions remain available here. Works own the Page structure and choose where elements belong."
            : "Name the areas in this Work's Page layout and choose which elements each area can contain."));
        toolbar.Children.Add(Button("Add location", (_, _) => Act(() => RefreshLists(regionId: _session.AddRegion().Id))));
        toolbar.Children.Add(Button("Remove", (_, _) => Act(() => { if (SelectedId(_regions) is { } id) _session.RemoveRegion(id); RefreshLists(); })));
        DockPanel.SetDock(toolbar, Dock.Top); left.Children.Add(toolbar); _regions = List("Element locations"); left.Children.Add(_regions);
        _regionDetails = new ContentControl(); _regions.SelectionChanged += (_, _) => ShowRegion(); return Split(left, _regionDetails);
    }

    private void ShowRegion()
    {
        var region = _session.Draft.Regions.FirstOrDefault(item => item.Id == SelectedId(_regions)); if (region is null) return;
        var panel = new StackPanel(); var name = EditText(region, "title", "Location name", region.Title, value => { region.Title = value; UpdateLabel(_regions, region.Id, value); });
        name.ToolTip = "Location ID: " + region.Id; panel.Children.Add(name);
        var all = new CheckBox { Content = "Allow all elements, including future additions", IsChecked = region.AllowedBlocks.Count == 0, Margin = new(0, 12, 0, 12) };
        all.Click += (_, _) => { region.AllowedBlocks = all.IsChecked == true ? [] : _session.Draft.Blocks.Select(block => block.Id).ToList(); ShowRegion(); Changed(); };
        panel.Children.Add(all); panel.Children.Add(Help("Choose allowed elements below. An empty allowed list means all elements in the current contract."));
        foreach (var block in _session.Draft.Blocks)
        {
            var check = new CheckBox { Content = block.Title, IsChecked = region.AllowedBlocks.Count == 0 || region.AllowedBlocks.Contains(block.Id), IsEnabled = region.AllowedBlocks.Count != 0, Margin = new(0, 5, 0, 5) };
            check.Click += (_, _) =>
            {
                if (check.IsChecked == true) { if (!region.AllowedBlocks.Contains(block.Id)) region.AllowedBlocks.Add(block.Id); }
                else if (region.AllowedBlocks.Count == 1) { check.IsChecked = true; _status.Text = "Keep one allowed element, or enable Allow all."; return; }
                else region.AllowedBlocks.Remove(block.Id);
                Changed();
            }; panel.Children.Add(check);
        }
        _regionDetails.Content = Scroll(panel);
    }

    private void RefreshLists(string? elementId = null, string? defaultId = null, string? regionId = null, string? styleId = null)
    {
        SetChoices(_elements, _session.Draft.Blocks.Select(block => new Choice(block.Id, block.Title)), elementId);
        SetChoices(_defaults, _session.Draft.DefaultBlocks.Select((block, index) => new Choice(block.Id,
            $"{index + 1}. {_session.Draft.Blocks.FirstOrDefault(schema => schema.Id == block.Type)?.Title ?? block.Type}")), defaultId);
        SetChoices(_regions, _session.Draft.Regions.Select(region => new Choice(region.Id, region.Title)), regionId);
        SetChoices(_styles, _session.Draft.Appearance.Select(field => new Choice(field.Id, field.Title)), styleId);
        ShowElement(); ShowDefault(); ShowRegion();
    }

    private void Changed() { if (!_ready) return; _dirty = true; Validate(); }
    private void Act(Action action)
    {
        try { action(); Changed(); }
        catch (Exception error) when (error is ContentValidationException or FormatException or InvalidOperationException) { _status.Text = error.Message; }
    }
    private void TryEdit(object owner, string key, Action action)
    {
        try { action(); _errors.Remove((owner, key)); }
        catch (Exception error) when (error is ContentValidationException or FormatException or JsonException or ArgumentException) { _errors[(owner, key)] = error.Message; }
        Changed();
    }
    private void Validate()
    {
        if (!_ready || _saving) return;
        if (_saveDesignGroup is not null)
        {
            _saveDesignGroup.IsEnabled = !_dirty && _libraryRoot is not null;
            _saveDesignGroup.ToolTip = _dirty ? "Save the Template before creating a Design Group." : "Copy this saved Template into the Core Library as a Design Group";
        }
        var owners = new HashSet<object> { _session.Draft };
        foreach (var block in _session.Draft.Blocks) { owners.Add(block); foreach (var field in block.Fields) owners.Add(field); }
        foreach (var field in _session.Draft.Appearance) owners.Add(field);
        foreach (var block in _session.Draft.DefaultBlocks) owners.Add(block);
        foreach (var region in _session.Draft.Regions) owners.Add(region);
        foreach (var key in _errors.Keys.Where(key => !owners.Contains(key.Owner)).ToList()) _errors.Remove(key);
        if (_errors.Count > 0) { _status.Text = _errors.Values.First(); _save.IsEnabled = false; return; }
        try { _session.Validate(); _status.Text = _dirty ? "Valid draft · changes are not saved" : "Template ready · existing content is checked before saving"; _save.IsEnabled = true; }
        catch (ContentValidationException error) { _status.Text = error.Message; _save.IsEnabled = false; }
    }
    private async void SaveClick(object sender, RoutedEventArgs args)
    {
        if (_saving || _errors.Count > 0) return;
        _saving = true; _save.IsEnabled = false; IsEnabled = false;
        try
        {
            var definition = _session.CreateDefinition();
            var compatibility = await HyperlibStudioAuthoringService.CheckCompatibilityAsync(_root, definition);
            _snapshot = await HyperlibTemplateObjectAuthoringService.CommitAsync(_root, _snapshot, definition, _importPlans);
            _importPlans.Clear(); _dirty = false;
            if (_saveDesignGroup is not null)
            {
                _saveDesignGroup.IsEnabled = _libraryRoot is not null;
                _saveDesignGroup.ToolTip = "Copy this saved Template into the Core Library as a Design Group";
            }
            _status.Text = (_reusableTemplate ? "Saved Template and collected objects. " : "Saved Work structure and collected objects. ") + compatibility.Message + " The last working renderer stays active until the new build passes.";
        }
        catch (Exception error) { _status.Text = error.Message; }
        finally { _saving = false; IsEnabled = true; _save.IsEnabled = _errors.Count == 0; }
    }

    private FrameworkElement EditText(object owner, string key, string label, string value, Action<string> update, bool multiline = false)
    {
        var text = new TextBox { Text = TextDraft(owner, key, value), Padding = new(8), MinWidth = 45, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multiline ? 80 : 36, MaxHeight = multiline ? 160 : double.PositiveInfinity, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden };
        AutomationProperties.SetName(text, label);
        text.TextChanged += (_, _) => { _textDrafts[(owner, key)] = text.Text; TryEdit(owner, key, () => update(text.Text)); };
        return Labelled(label, text);
    }
    private string TextDraft(object owner, string key, string value) => _textDrafts.TryGetValue((owner, key), out var draft) ? draft : value;
    private static double? OptionalNumber(string value) => string.IsNullOrWhiteSpace(value) ? null : HyperlibTemplateEditSession.ParseValue("number", value).GetDouble();
    private Button ColorButton(string label, Func<string> current, Action<string> changed) => Button("Choose color…", (_, _) =>
    {
        var initial = current();
        // CSS eight-digit colors use trailing alpha; the existing picker edits RGB.
        if (initial.Length == 9 && initial[0] == '#') initial = initial[..7];
        var picker = new ColorPickerDialog(label, initial) { Owner = this };
        if (picker.ShowDialog() == true) changed(picker.SelectedColor);
    }, "Choose " + label + " color");
    private static FrameworkElement Labelled(string text, FrameworkElement control)
    {
        var panel = new StackPanel { Margin = new(0, 5, 0, 7) };
        panel.Children.Add(new Label { Content = text, Target = control, Padding = new(0, 0, 0, 5), FontSize = 12 }); panel.Children.Add(control); return panel;
    }
    private static Brush Theme(string key, string fallback) => Application.Current?.TryFindResource(key) as Brush ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
    private static TextBlock Heading(string text, double size = 17) => new() { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) };
    private static TextBlock Help(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Theme("MutedForegroundBrush", "#929AA6"), FontSize = 12, Margin = new(0, 5, 0, 10), LineHeight = 18 };
    private static Button Button(string text, RoutedEventHandler click, string? name = null)
    {
        var button = new Button { Content = text, Padding = new(11, 7, 11, 7), Margin = new(0, 4, 8, 4), MinHeight = 32 };
        button.Click += click; AutomationProperties.SetName(button, name ?? text); button.ToolTip = name; return button;
    }
    private static ComboBox ChoiceBox(System.Collections.IEnumerable choices, string label, string? display = null)
    {
        var combo = new ComboBox { ItemsSource = choices, MinHeight = 34, Margin = new(0, 3, 0, 5), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        if (display is not null) combo.DisplayMemberPath = display;
        combo.SelectedIndex = 0; AutomationProperties.SetName(combo, label); return combo;
    }
    private static ListBox List(string label)
    {
        var list = new ListBox { Background = Theme("PanelBrush", "#151A20"), BorderThickness = new(0), Padding = new(5), Margin = new(0, 8, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = nameof(Choice.Label), MinHeight = 75 };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled); AutomationProperties.SetName(list, label); return list;
    }
    private static string? SelectedId(ListBox list) => (list.SelectedItem as Choice)?.Id;
    private static void UpdateLabel(ListBox list, string id, string text)
    {
        if (list.Items.OfType<Choice>().FirstOrDefault(choice => choice.Id == id) is { } choice) choice.Label = text;
    }
    private static void SetChoices(ListBox list, IEnumerable<Choice> choices, string? id = null)
    {
        id ??= SelectedId(list); var items = choices.ToList(); list.ItemsSource = items; list.SelectedItem = items.FirstOrDefault(item => item.Id == id) ?? items.FirstOrDefault();
    }
    private static FrameworkElement Split(FrameworkElement left, FrameworkElement right, double leftWidth = 220)
    {
        var grid = new Grid { Margin = new(12), MinHeight = 160 };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(leftWidth), MinWidth = Math.Min(leftWidth, 170) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 });
        Grid.SetColumn(right, 2); grid.Children.Add(left); grid.Children.Add(right); return grid;
    }
    private static ScrollViewer Scroll(FrameworkElement child) => new() { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 12, 0) };
    private sealed class Choice(string id, string label) : INotifyPropertyChanged
    {
        public string Id { get; } = id;
        private string _label = label;
        public string Label { get => _label; set { if (_label == value) return; _label = value; PropertyChanged?.Invoke(this, new(nameof(Label))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
