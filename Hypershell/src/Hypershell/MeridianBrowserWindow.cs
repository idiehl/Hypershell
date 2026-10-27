using Hypershell.Services;
using Hypershell.ViewModels;
using Microsoft.Web.WebView2.Core;
using System.Windows;
using System.Windows.Data;

namespace Hypershell;

/// <summary>A website reader, isolated from Meridian's trusted local document.</summary>
public sealed class MeridianBrowserWindow : Window
{
    private readonly BrowserTabViewModel _browser;
    private CoreWebView2? _core;

    public MeridianBrowserWindow(string address, SettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!IsWebsiteAddress(address))
            throw new InvalidOperationException("The Meridian browser requires an HTTP or HTTPS website address.");

        Width = 1120;
        Height = 820;
        MinWidth = 420;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackgroundBrush");
        SetResourceReference(ForegroundProperty, "ForegroundBrush");
        SetResourceReference(FontFamilyProperty, "UiFontFamily");

        // No managed-app NavigationPolicy or app-host script is inherited.
        // The separate InPrivate profile cannot read the editor's cookies.
        _browser = new BrowserTabViewModel(address,
            userDataFolder: MyNetAppWindow.GetBrowserDataDirectory(settings, "atlas.meridian.websites"),
            isInPrivateModeEnabled: true, showNavigationToolbar: true)
        {
            UseDocumentTitle = true
        };
        _browser.BrowserView.ConfigureAppHostAsync = ConfigureBrowserAsync;
        DataContext = _browser;
        SetBinding(TitleProperty, new Binding(nameof(BrowserTabViewModel.Title)));
        Content = _browser.BrowserView;
        Closed += BrowserClosed;
    }

    internal BrowserTabViewModel BrowserModel => _browser;

    internal static bool IsWebsiteAddress(string? address) =>
        address is { Length: <= 8192 }
        && Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && !string.IsNullOrWhiteSpace(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo);

    private Task ConfigureBrowserAsync(CoreWebView2 core)
    {
        _core = core;
        core.Settings.IsWebMessageEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.NavigationStarting += WebsiteNavigationStarting;
        return Task.CompletedTask;
    }

    private void WebsiteNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (IsWebsiteAddress(args.Uri)) return;
        args.Cancel = true;
        _browser.ReportError("This browser opens HTTP and HTTPS websites. Other address types remain outside the website reader.");
    }

    private void BrowserClosed(object? sender, EventArgs args)
    {
        Closed -= BrowserClosed;
        if (_core is not null)
        {
            _core.NavigationStarting -= WebsiteNavigationStarting;
            _core = null;
        }
        Content = null;
        _browser.BrowserView.ConfigureAppHostAsync = null;
        _browser.Dispose();
    }
}
