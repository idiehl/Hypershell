using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Hypershell.Models;
using Hypershell.ViewModels;
using Hypershell.Services;

namespace Hypershell;

/// <summary>Return to a project's toolset and reusable layouts without editing existing Page content.</summary>
public sealed class HyperlibProjectStagingDialog : Window
{
    private readonly HyperlibWorkWizardViewModel _wizard;
    private readonly Button _save;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 10, 0, 0) };
    private bool _busy;
    private bool _allowClose;
    public HyperlibWorkProjectDefinition? Definition { get; private set; }
    public HyperlibProjectStagingDialog(string libraryRoot, HyperlibWorkProjectDefinition definition,
        Func<HyperlibWorkProjectDefinition, Task>? saveAsync = null, bool startAtStaging = false,
        IReadOnlyList<HyperlibProjectToolkitItem>? savedToolkit = null, string? ownedProjectRoot = null)
    {
        _wizard = new HyperlibWorkWizardViewModel(libraryRoot, savedToolkit, ownedProjectRoot); _wizard.LoadDefinition(definition); DataContext = _wizard;
        // Every entry opens on the landing page's Background step; the old boolean
        // remains source compatible with callers that previously skipped shopping.
        _wizard.Step = 0;
        var initialDraft = _wizard.CaptureDraftState();
        Title = "Build your site · " + definition.WorkTitle;
        Width = 1180; Height = 860; MinWidth = 620; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush?)Application.Current?.TryFindResource("AppBackgroundBrush") ?? Brushes.Black;
        Foreground = (Brush?)Application.Current?.TryFindResource("ForegroundBrush") ?? Brushes.White;
        var view = new DockPanel { Margin = new(24) };
        var footer = new DockPanel { Margin = new(0, 18, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = new Button { Content = "Cancel", Padding = new(16, 9, 16, 9), Margin = new(0, 0, 8, 0) }; cancel.Click += (_, _) => Close();
        var back = new Button { Content = "Back", Padding = new(16, 9, 16, 9), Margin = new(0, 0, 8, 0) }; back.SetBinding(Button.CommandProperty, new Binding(nameof(_wizard.BackCommand)));
        var next = new Button { Padding = new(16, 9, 16, 9), Margin = new(0, 0, 8, 0) }; next.SetBinding(Button.ContentProperty, new Binding(nameof(_wizard.NextLabel))); next.SetBinding(Button.CommandProperty, new Binding(nameof(_wizard.NextCommand))); next.SetBinding(VisibilityProperty, new Binding(nameof(_wizard.ShowNext)) { Converter = new BooleanToVisibilityConverter() });
        _save = new Button { Content = "Save foundation", Padding = new(18, 9, 18, 9) }; _save.SetBinding(VisibilityProperty, new Binding(nameof(_wizard.IsReviewStep)) { Converter = new BooleanToVisibilityConverter() });
        async Task SaveChangesAsync()
        {
            if (!_wizard.IsValid || _busy) return;
            _busy = true; view.IsEnabled = false;
            try
            {
                var updated = _wizard.BuildDefinition();
                if (saveAsync is not null) await saveAsync(updated);
                Definition = updated; _allowClose = true; DialogResult = true;
            }
            catch (Exception error) { _status.Text = "The foundation could not be saved: " + error.Message; }
            finally { _busy = false; view.IsEnabled = true; }
        }
        _save.Click += async (_, _) => await SaveChangesAsync();
        actions.Children.Add(cancel); actions.Children.Add(back); actions.Children.Add(next); actions.Children.Add(_save);
        DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
        var note = new StackPanel(); note.Children.Add(new TextBlock { Text = "Shared layout changes update your Pages while preserving their content.", TextWrapping = TextWrapping.Wrap, MaxWidth = 300 }); note.Children.Add(_status); footer.Children.Add(note);
        DockPanel.SetDock(footer, Dock.Bottom); view.Children.Add(footer);
        var builder = new HyperlibWorkWizardView { DataContext = _wizard };
        view.Children.Add(builder);
        Content = view;
        _wizard.PropertyChanged += (_, _) => _save.IsEnabled = _wizard.IsValid && !_busy;
        Closing += (_, args) =>
        {
            if (_allowClose) return;
            if (_busy) { args.Cancel = true; return; }
            var dirty = _wizard.CaptureDraftState() != initialDraft;
            if (!dirty) return;
            args.Cancel = true;
            var choice = MessageBox.Show(this, "Save changes to the project toolset and Page templates?", "Unsaved foundation changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.No) { _allowClose = true; args.Cancel = false; }
            else if (choice == MessageBoxResult.Yes)
            {
                if (!_wizard.IsValid) _status.Text = _wizard.ValidationMessage;
                else Dispatcher.BeginInvoke(new Action(async () => await SaveChangesAsync()));
            }
        };
    }
}
