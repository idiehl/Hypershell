using Hypershell.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Hypershell;

public partial class NetworkClientTabView : UserControl
{
    private readonly NetworkClientTabViewModel _viewModel;
    private bool _autoConnectAttempted;

    public NetworkClientTabView(NetworkClientTabViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private async void NetworkClientTabLoaded(
        object sender,
        RoutedEventArgs eventArgs)
    {
        if (_autoConnectAttempted || !_viewModel.AutoConnect)
        {
            return;
        }

        _autoConnectAttempted = true;
        await ConnectWithConfirmationAsync();
    }

    private async void ConnectClick(object sender, RoutedEventArgs eventArgs) =>
        await ConnectWithConfirmationAsync();

    private async Task ConnectWithConfirmationAsync()
    {
        try
        {
            if (await _viewModel.RequiresPublicConfirmationAsync())
            {
                var result = MessageBox.Show(
                    Window.GetWindow(this),
                    $"Open a {_viewModel.Transport.ToString().ToUpperInvariant()} connection to "
                    + $"{_viewModel.Host}:{_viewModel.PortText}?\n\n"
                    + "This destination resolves to the public network. Atlas MyNet will not send a payload until you press Send.",
                    "Confirm public network connection",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            await _viewModel.ConnectAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                exception.Message,
                "Unable to connect",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void DisconnectClick(object sender, RoutedEventArgs eventArgs) =>
        _viewModel.Disconnect();

    private async void SendClick(object sender, RoutedEventArgs eventArgs) =>
        await _viewModel.SendAsync();

    private void ClearClick(object sender, RoutedEventArgs eventArgs) =>
        _viewModel.ClearTranscript();

    private void SaveClick(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Atlas MyNet transcript",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = ".txt",
            AddExtension = true,
            FileName = $"atlas-mynet-{_viewModel.Transport.ToString().ToLowerInvariant()}-transcript.txt"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _viewModel.CreateTranscriptText());
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                exception.Message,
                "Unable to save transcript",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
