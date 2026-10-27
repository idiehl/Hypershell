using Hypershell.ViewModels;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Hypershell.Controls;

public partial class ProjectWorkItemsView : UserControl
{
    private Point _dragStart;
    private ProjectWorkItemViewModel? _draggedItem;

    public ProjectWorkItemsView()
    {
        InitializeComponent();
    }

    private ProjectWorkItemsViewModel? ViewModel =>
        DataContext as ProjectWorkItemsViewModel;

    private void AddDraftAttachmentsClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        var paths = SelectAttachmentFiles();
        if (paths.Count > 0)
        {
            ViewModel?.AddAttachmentFiles(null, paths);
        }
    }

    private void AddItemAttachmentsClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not FrameworkElement
            {
                Tag: ProjectWorkItemViewModel item
            })
        {
            return;
        }

        var paths = SelectAttachmentFiles();
        if (paths.Count > 0)
        {
            ViewModel?.AddAttachmentFiles(item, paths);
        }
    }

    private void OpenAttachmentClick(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (sender is not FrameworkElement
            {
                Tag: ProjectWorkItemAttachmentViewModel attachment
            }
            || ViewModel is not { } viewModel)
        {
            return;
        }

        try
        {
            var path = viewModel.ResolveAttachmentPath(attachment);
            if (!File.Exists(path))
            {
                return;
            }

            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or InvalidOperationException)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                exception.Message,
                "Open work-item attachment",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void WorkItemListPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs eventArgs)
    {
        _dragStart = eventArgs.GetPosition(WorkItemList);
        var source = eventArgs.OriginalSource as DependencyObject;
        _draggedItem = IsInteractiveSource(source)
            ? null
            : FindItem(source);
    }

    private void WorkItemListPreviewMouseMove(
        object sender,
        MouseEventArgs eventArgs)
    {
        if (eventArgs.LeftButton != MouseButtonState.Pressed
            || _draggedItem is null)
        {
            return;
        }

        var current = eventArgs.GetPosition(WorkItemList);
        if (Math.Abs(current.X - _dragStart.X)
                < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y)
                < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(
            WorkItemList,
            new DataObject(typeof(ProjectWorkItemViewModel), _draggedItem),
            DragDropEffects.Move);
        _draggedItem = null;
    }

    private void WorkItemListDragOver(
        object sender,
        DragEventArgs eventArgs)
    {
        eventArgs.Effects = eventArgs.Data.GetDataPresent(
            typeof(ProjectWorkItemViewModel))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        eventArgs.Handled = true;
    }

    private void WorkItemListDrop(
        object sender,
        DragEventArgs eventArgs)
    {
        if (eventArgs.Data.GetData(typeof(ProjectWorkItemViewModel))
                is not ProjectWorkItemViewModel source)
        {
            return;
        }

        var target = FindItem(eventArgs.OriginalSource as DependencyObject);
        if (target is not null)
        {
            ViewModel?.MoveItem(source, target);
        }

        eventArgs.Handled = true;
    }

    private ProjectWorkItemViewModel? FindItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ListBoxItem
                {
                    DataContext: ProjectWorkItemViewModel item
                })
            {
                return item;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static bool IsInteractiveSource(DependencyObject? source)
    {
        while (source is not null && source is not ListBoxItem)
        {
            if (source is ButtonBase or TextBoxBase or ComboBox)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static IReadOnlyList<string> SelectAttachmentFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Attach files to the work item",
            Filter = "All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true
            ? dialog.FileNames
            : [];
    }
}
