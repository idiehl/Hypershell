using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hypershell.ViewModels;
using Microsoft.Win32;

namespace Hypershell;

public partial class StudioProjectStartView : UserControl
{
    private StudioProjectStartTabViewModel? _boundModel;
    public StudioProjectStartView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindModel(DataContext as StudioProjectStartTabViewModel);
        Loaded += (_, _) => BindModel(DataContext as StudioProjectStartTabViewModel);
        Unloaded += (_, _) => BindModel(null);
    }
    public StudioProjectStartView(StudioProjectStartTabViewModel viewModel) : this() => DataContext = viewModel;

    private void BindModel(StudioProjectStartTabViewModel? model)
    {
        if (ReferenceEquals(model, _boundModel)) return;
        if (_boundModel is not null)
        {
            _boundModel.PropertyChanged -= ModelPropertyChanged;
            _boundModel.Wizard.PropertyChanged -= WizardPropertyChanged;
        }
        _boundModel = model;
        if (model is not null)
        {
            model.PropertyChanged += ModelPropertyChanged;
            model.Wizard.PropertyChanged += WizardPropertyChanged;
        }
    }
    private void ModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(StudioProjectStartTabViewModel.IsWizardVisible) or nameof(StudioProjectStartTabViewModel.IsTemplateGalleryVisible)) ScrollToStart();
    }
    private void WizardPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(HyperlibWorkWizardViewModel.Step)) ScrollToStart();
    }
    private void ScrollToStart() => Dispatcher.BeginInvoke(() => StartContentScrollViewer.ScrollToTop(), DispatcherPriority.Loaded);

    private void ChooseLocationClick(object sender, RoutedEventArgs args)
    {
        if (DataContext is not StudioProjectStartTabViewModel model || !model.IsEditable) return;
        var dialog = new OpenFolderDialog { Title = "Choose where to create the project", Multiselect = false };
        if (Directory.Exists(model.ParentDirectory)) dialog.InitialDirectory = model.ParentDirectory;
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) model.ParentDirectory = dialog.FolderName;
    }
    private async void OpenProjectClick(object sender, RoutedEventArgs args)
    {
        if (DataContext is not StudioProjectStartTabViewModel model || !model.IsEditable) return;
        var dialog = new OpenFolderDialog { Title = "Open a Hyperlib, Razor Pages, or HTML project", Multiselect = false };
        if (Directory.Exists(model.ParentDirectory)) dialog.InitialDirectory = model.ParentDirectory;
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await model.OpenExistingProjectAsync(dialog.FolderName);
    }
}
