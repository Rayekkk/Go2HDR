using Go2HDR.Services;
using Go2HDR.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Go2HDR;

public partial class MainWindow : FluentWindow
{
    private readonly DispatcherTimer _displayRefreshTimer;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        _displayRefreshTimer = new DispatcherTimer();
        _displayRefreshTimer.Tick += (_, _) =>
        {
            _displayRefreshTimer.Stop();
            var display = App.Services.GetRequiredService<DisplayConfigService>();
            display.InvalidateCache();
            App.Services.GetRequiredService<HdrService>().Poll(forceRefresh: true);
        };
        SystemThemeWatcher.Watch(this);
        Loaded += (_, _) =>
        {
            RootNavigation.SetServiceProvider(App.Services);
            RootNavigation.Navigate(typeof(DashboardPage));
        };
        AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel), handledEventsToo: true);
    }

    private const int WM_DISPLAYCHANGE = 0x007E;
    private const int WM_POWERBROADCAST = 0x0218;
    private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    private const int PBT_APMRESUMESUSPEND = 0x0007;

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
            ScheduleDisplayRefresh(TimeSpan.FromMilliseconds(350));
        }
        else if (msg == WM_POWERBROADCAST)
        {
            int ev = wParam.ToInt32();
            if (ev == PBT_APMRESUMEAUTOMATIC || ev == PBT_APMRESUMESUSPEND)
            {
                ScheduleDisplayRefresh(TimeSpan.FromSeconds(1));
            }
        }
        return IntPtr.Zero;
    }

    private void ScheduleDisplayRefresh(TimeSpan delay)
    {
        App.Services.GetRequiredService<DisplayConfigService>().InvalidateCache();
        _displayRefreshTimer.Stop();
        _displayRefreshTimer.Interval = delay;
        _displayRefreshTimer.Start();
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
        if (_isExiting) return;
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
        App.Services.GetRequiredService<HdrService>().Poll(forceRefresh: true);
    }

    private void OnTrayExit(object sender, RoutedEventArgs e)
    {
        _isExiting = true;
        _displayRefreshTimer.Stop();
        TrayIcon.Unregister();
        Application.Current.Shutdown();
    }
}
