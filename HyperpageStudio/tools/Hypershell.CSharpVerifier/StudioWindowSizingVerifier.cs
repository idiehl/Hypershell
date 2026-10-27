using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Hypershell;
using Hypershell.Services;
using Hypershell.ViewModels;

/// <summary>Real standalone window, native sizing messages, and client chrome; never shows or activates an HWND.</summary>
public static class StudioWindowSizingVerifier
{
    public static async Task VerifyAsync(string outputDirectory,string repositoryRoot)
    {
        var fixture=Path.Combine(Path.GetFullPath(outputDirectory),"studio-window-sizing-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);var receipt=new Receipt{Fixture=fixture};
        var variables=new[]{SettingsService.ApplicationDataOverrideVariable,SettingsService.SharedMyNetDirectoryOverrideVariable,StandaloneStudioHost.BaseDataOverrideVariable,"HYPERLIB_ROOT"};var previous=variables.ToDictionary(name=>name,Environment.GetEnvironmentVariable);
        try
        {
            var profile=Path.Combine(fixture,"profile");Environment.SetEnvironmentVariable(SettingsService.ApplicationDataOverrideVariable,profile);Environment.SetEnvironmentVariable(SettingsService.SharedMyNetDirectoryOverrideVariable,Path.Combine(profile,"AtlasMyNet"));Environment.SetEnvironmentVariable(StandaloneStudioHost.BaseDataOverrideVariable,profile);Environment.SetEnvironmentVariable("HYPERLIB_ROOT",Path.Combine(fixture,"unavailable-library"));
            var settings=new SettingsService(profile);var preferences=settings.Load();preferences.Application.CreateTerminalOnStartup=false;preferences.Explorer.StartupDirectory=fixture;settings.Save(preferences);
            await RunStaAsync(async()=>{LoadResources(repositoryRoot);await VerifyWindowAsync(fixture,receipt);});receipt.Passed=true;
        }
        catch(Exception error){receipt.Failure=error.ToString();throw;}
        finally{foreach(var pair in previous)Environment.SetEnvironmentVariable(pair.Key,pair.Value);await File.WriteAllTextAsync(Path.Combine(fixture,"studio-window-sizing-verification.json"),JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));}
        Console.WriteLine($"STUDIO_WINDOW_SIZING_OK|checks={receipt.Assertions.Count}|renders={receipt.Renders.Count}|hidden-real-window|fixture={fixture}");
    }
    private static async Task VerifyWindowAsync(string fixture,Receipt receipt)
    {
        var foreground=GetForegroundWindow();var window=new MainWindow(StandaloneStudioMode.HyperpageStudio){WindowStartupLocation=WindowStartupLocation.Manual,ShowActivated=false,ShowInTaskbar=false,Left=-32000,Top=-32000};HwndSource? source=null;HwndSourceHook? hook=null;
        try
        {
            // The production source initialization also registers a global AI stop hotkey.
            // This fixture uses the exact production sizing hook, without registering that unrelated hotkey.
            window.SourceInitialized-=(EventHandler)Delegate.CreateDelegate(typeof(EventHandler),window,Private("OnSourceInitialized"));
            window.Loaded-=(RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler),window,Private("OnLoaded"));
            var model=(MainWindowViewModel)window.DataContext;await model.InitializeAsync();await model.FileExplorer.WhenIdleAsync();Check(receipt,window.MinWidth==640&&window.MinHeight==480&&model.IsStandaloneStudio,"The actual standalone MainWindow accepts a640 by480-DIP minimum using a private settings profile.");
            var surface=(FrameworkElement)window.Content;Arrange(surface,new Size(1280,760));var tabs=(TabControl)window.FindName("WorkspaceTabs");tabs.ApplyTemplate();Arrange(surface,new Size(1280,760));
            var minimize=ChromeButton(tabs,"StudioMinimizeWindowButton");var maximize=ChromeButton(tabs,"StudioMaximizeRestoreWindowButton");var close=ChromeButton(tabs,"StudioCloseWindowButton");var helper=new WindowInteropHelper(window);
            Check(receipt,helper.Handle==IntPtr.Zero,"Real window construction and chrome realization remain HWND-free before the hidden native sizing phase.");
            minimize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(receipt,window.WindowState==WindowState.Minimized,"The actual Minimize button handler changes the unshown window state to Minimized.");maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(receipt,window.WindowState==WindowState.Maximized,"The actual maximize handler changes the unshown window state to Maximized.");maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(receipt,window.WindowState==WindowState.Normal&&helper.Handle==IntPtr.Zero,"The actual restore handler returns to Normal without showing or creating a desktop window.");
            var handle=helper.EnsureHandle();source=HwndSource.FromHwnd(handle)??throw new InvalidOperationException("The real window has no native presentation source.");hook=(HwndSourceHook)Delegate.CreateDelegate(typeof(HwndSourceHook),window,Private("OnWindowMessage"));source.AddHook(hook);
            var style=GetWindowLongPtr(handle,-20).ToInt64();SetWindowLongPtr(handle,-20,new IntPtr(style|0x08000000L|0x00000080L));
            Check(receipt,!IsWindowVisible(handle),"The real native MainWindow handle is hidden, off-screen, a tool window, and never activated.");
            var dpi=VisualTreeHelper.GetDpi(window);var pointer=Marshal.AllocHGlobal(Marshal.SizeOf<MinMaxInfo>());
            try
            {
                Marshal.StructureToPtr(new MinMaxInfo(),pointer,false);SendMessage(handle,0x0024,IntPtr.Zero,pointer);var limits=Marshal.PtrToStructure<MinMaxInfo>(pointer);
                Check(receipt,limits.MinTrackSize.X==Math.Min(limits.MaxSize.X,(int)Math.Ceiling(640*dpi.DpiScaleX))&&limits.MinTrackSize.Y==Math.Min(limits.MaxSize.Y,(int)Math.Ceiling(480*dpi.DpiScaleY)),"WM_GETMINMAXINFO reaches the production window hook and reports the DPI-adjusted640 by480 minimum.");
                Check(receipt,limits.MaxSize.X>limits.MinTrackSize.X&&limits.MaxSize.Y>limits.MinTrackSize.Y&&limits.MaxTrackSize.X==limits.MaxSize.X&&limits.MaxTrackSize.Y==limits.MaxSize.Y,"Native maximize/maximum tracking dimensions use the monitor work area rather than the general shell minimum.");receipt.NativeLimits=new(){MinWidth=limits.MinTrackSize.X,MinHeight=limits.MinTrackSize.Y,MaxWidth=limits.MaxSize.X,MaxHeight=limits.MaxSize.Y,DpiX=dpi.DpiScaleX,DpiY=dpi.DpiScaleY};
            }
            finally{Marshal.FreeHGlobal(pointer);}
            var start=(StudioProjectStartTabViewModel)model.ActiveWorkspaceTab!;start.ProjectName="Header sizing";start.ParentDirectory=Path.Combine(fixture,"projects");Directory.CreateDirectory(start.ParentDirectory);
            var startView=Descendants<StudioProjectStartView>(surface).Single();var wizard=(HyperlibWorkWizardView)startView.FindName("WorkWizard");((HyperlibGuidedPagePreview)wizard.FindName("PageBuilderPreview")).Dispose();start.BeginWizardCommand.Execute(null);start.Wizard.Step=1;var block=start.Wizard.CurrentTemplate.HeaderLayout.AddBlock();start.Wizard.CurrentTemplate.HeaderLayout.AddElement(block,"text").Text="A compact Studio header";
            foreach(var width in new[]{640d,800d,960d,1280d})
            {
                window.Width=width;window.Height=760;SetWindowPos(handle,IntPtr.Zero,-32000,-32000,(int)Math.Ceiling(width*dpi.DpiScaleX),(int)Math.Ceiling(760*dpi.DpiScaleY),0x0004|0x0010|0x0200);window.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
                if(!GetClientRect(handle,out var client))throw new InvalidOperationException("Cannot read hidden MainWindow client bounds.");var clientSize=new Size((client.Right-client.Left)/dpi.DpiScaleX,(client.Bottom-client.Top)/dpi.DpiScaleY);Arrange(surface,clientSize);
                Check(receipt,Math.Abs(clientSize.Width-width)<=2&&surface.ActualWidth<=clientSize.Width+.1,"The real hidden native window and its client content resize together at"+width+"DIP.");
                foreach(var button in new[]{minimize,maximize,close}){var bounds=button.TransformToAncestor(surface).TransformBounds(new Rect(button.RenderSize));Check(receipt,button.Visibility==Visibility.Visible&&bounds.Width>=25&&bounds.Height>=25&&bounds.Left>=0&&bounds.Top>=0&&bounds.Right<=clientSize.Width+.1&&bounds.Bottom<=clientSize.Height+.1,"The "+button.ToolTip+" window control remains fully bounded at"+width+"DIP.");}
                var startBounds=startView.TransformToAncestor(surface).TransformBounds(new Rect(startView.RenderSize));Check(receipt,startBounds.Left>=0&&startBounds.Top>=0&&startBounds.Right<=clientSize.Width+.1&&startBounds.Bottom<=clientSize.Height+.1,"The actual Studio project/wizard surface remains inside the client at"+width+"DIP.");
                var preview=(FrameworkElement)wizard.FindName("PreviewPanel");var previewBounds=preview.TransformToAncestor(surface).TransformBounds(new Rect(preview.RenderSize));Check(receipt,previewBounds.Width>150&&previewBounds.Left>=0&&previewBounds.Right<=clientSize.Width+.1,"The actual header Page builder adapts within the compact client at"+width+"DIP.");
                Render(surface,Path.Combine(fixture,$"studio-client-{width:0}.png"),clientSize,receipt);receipt.ClientWidths.Add(clientSize.Width);Check(receipt,!IsWindowVisible(handle)&&GetForegroundWindow()==foreground,"Resizing the isolated hidden window leaves the user's foreground window unchanged.");
            }
        }
        finally
        {
            if(source is not null&&hook is not null)source.RemoveHook(hook);
            Private("FinalizeWindowClose").Invoke(window,null);typeof(MainWindow).GetField("_isCloseApproved",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(window,true);window.Close();
        }
    }
    private static MethodInfo Private(string name)=>typeof(MainWindow).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly)??throw new InvalidOperationException("Missing MainWindow."+name);
    private static Button ChromeButton(TabControl tabs,string name)=>tabs.Template.FindName(name,tabs)as Button??throw new InvalidOperationException("Missing actual window control "+name);
    private static void Arrange(FrameworkElement view,Size size){view.Measure(size);view.Arrange(new Rect(new Point(),size));view.UpdateLayout();view.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);view.UpdateLayout();}
    private static IEnumerable<T> Descendants<T>(DependencyObject root)where T:DependencyObject{for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++){var child=VisualTreeHelper.GetChild(root,index);if(child is T typed)yield return typed;foreach(var nested in Descendants<T>(child))yield return nested;}}
    private static void Check(Receipt receipt,bool condition,string message){if(!condition)throw new InvalidOperationException(message);receipt.Assertions.Add(message);}
    private static void Render(FrameworkElement view,string path,Size size,Receipt receipt){var bitmap=new RenderTargetBitmap((int)Math.Ceiling(size.Width),(int)Math.Ceiling(size.Height),96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);receipt.Renders.Add(path);}
    private static Task RunStaAsync(Func<Task> action){var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var thread=new Thread(()=>{var dispatcher=Dispatcher.CurrentDispatcher;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));dispatcher.BeginInvoke(async()=>{try{await action();completion.TrySetResult();}catch(Exception error){completion.TrySetException(error);}finally{dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);}});Dispatcher.Run();}){IsBackground=true,Name="Studio real window sizing acceptance"};thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;}
    private static void LoadResources(string repository)
    {
        if(Application.Current is not null)return;XNamespace presentation="http://schemas.microsoft.com/winfx/2006/xaml/presentation";var source=XDocument.Load(Path.Combine(repository,"src/Hypershell/App.xaml")).Root!;var dictionary=new XElement(presentation+"ResourceDictionary",source.Attributes().Where(attribute=>attribute.IsNamespaceDeclaration).Select(attribute=>new XAttribute(attribute)),source.Element(presentation+"Application.Resources")!.Elements().Select(element=>new XElement(element)));
        foreach(var attribute in dictionary.DescendantsAndSelf().Attributes().Where(attribute=>attribute.IsNamespaceDeclaration))if(attribute.Value.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!attribute.Value.Contains(";assembly=",StringComparison.Ordinal))attribute.Value+=";assembly=Hypershell";
        foreach(var element in dictionary.DescendantsAndSelf())if(element.Name.NamespaceName.StartsWith("clr-namespace:Hypershell",StringComparison.Ordinal)&&!element.Name.NamespaceName.Contains(";assembly=",StringComparison.Ordinal))element.Name=XName.Get(element.Name.LocalName,element.Name.NamespaceName+";assembly=Hypershell");dictionary.Descendants().Attributes("Click").Remove();new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown,Resources=(ResourceDictionary)XamlReader.Parse(dictionary.ToString())};
    }
    [StructLayout(LayoutKind.Sequential)]private struct PointNative{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct MinMaxInfo{public PointNative Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}
    [StructLayout(LayoutKind.Sequential)]private struct RectNative{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetClientRect(IntPtr handle,out RectNative rect);
    [DllImport("user32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetWindowPos(IntPtr handle,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern IntPtr GetWindowLongPtr(IntPtr handle,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern IntPtr SetWindowLongPtr(IntPtr handle,int index,IntPtr value);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr wParam,IntPtr lParam);
    private sealed class Receipt{public bool Passed{get;set;}public string Fixture{get;set;}="";public List<string> Assertions{get;}=[];public List<double> ClientWidths{get;}=[];public List<string> Renders{get;}=[];public NativeLimitReceipt? NativeLimits{get;set;}public string? Failure{get;set;}}
    private sealed class NativeLimitReceipt{public int MinWidth{get;set;}public int MinHeight{get;set;}public int MaxWidth{get;set;}public int MaxHeight{get;set;}public double DpiX{get;set;}public double DpiY{get;set;}}
}
