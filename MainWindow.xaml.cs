using Go2HDR.Services;
using Go2HDR.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Go2HDR;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this);
        Loaded += (_, _) =>
        {
            RootNavigation.SetServiceProvider(App.Services);
            RootNavigation.Navigate(typeof(DashboardPage));
        };
        AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel), handledEventsToo: true);
    }

    private const int WM_DISPLAYCHANGE       = 0x007E;
    private const int WM_POWERBROADCAST      = 0x0218;
    private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    private const int PBT_APMRESUMESUSPEND   = 0x0007;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE)
        {
            var display = App.Services.GetRequiredService<DisplayConfigService>();
            var hdr     = App.Services.GetRequiredService<HdrService>();
            display.InvalidateCache();
            hdr.Poll();
        }
        else if (msg == WM_POWERBROADCAST)
        {
            int ev = wParam.ToInt32();
            if (ev == PBT_APMRESUMEAUTOMATIC || ev == PBT_APMRESUMESUSPEND)
            {
                var display = App.Services.GetRequiredService<DisplayConfigService>();
                var hdr     = App.Services.GetRequiredService<HdrService>();
                display.InvalidateCache();
                _ = Task.Delay(1000).ContinueWith(_ => Dispatcher.Invoke(hdr.Poll));
            }
        }
        return IntPtr.Zero;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;

        var sv = FindScrollableViewer(src);
        if (sv == null) return;

        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 2.0);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollableViewer(DependencyObject src)
    {
        var el = VisualTreeHelper.GetParent(src);
        while (el != null)
        {
            if (el is ScrollViewer sv && sv.ScrollableHeight > 0) return sv;
            el = VisualTreeHelper.GetParent(el);
        }
        return null;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        var settings = App.Services.GetRequiredService<SettingsService>();
        if (WindowState == WindowState.Minimized && settings.Current.MinimizeToTray)
            HideToTray();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var settings = App.Services.GetRequiredService<SettingsService>();
        if (settings.Current.MinimizeToTray)
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        WindowState = WindowState.Minimized;
        Hide();
    }

    private void OnTrayOpen(object sender, RoutedEventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
        App.Services.GetRequiredService<DisplayConfigService>().InvalidateCache();
        App.Services.GetRequiredService<HdrService>().Poll();
    }

    private void OnTrayExit(object sender, RoutedEventArgs e)
    {
        TrayIcon.Unregister();
        Application.Current.Shutdown();
    }
}
