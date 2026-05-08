using Go2HDR.Services;
using Go2HDR.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        Loaded += (_, _) => RootNavigation.Navigate(typeof(DashboardPage));
        AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel), handledEventsToo: true);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject src) return;
        if (IsInsideScrollConsumer(src)) return;

        var sv = FindScrollableViewer(src);
        if (sv == null) return;

        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 2.0);
        e.Handled = true;
    }

    // Walk up the visual tree and return the first ScrollViewer that actually has scrollable
    // content (ScrollableHeight > 0).  This skips empty inner SVs (e.g. CardExpander template)
    // and lands on the real scrolling container whether that is the page's RootScroller or the
    // DataGrid's internal ScrollViewer.
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

    private static bool IsInsideScrollConsumer(DependencyObject src)
    {
        var el = src;
        while (el != null)
        {
            if (el is Wpf.Ui.Controls.NumberBox) return true;
            el = VisualTreeHelper.GetParent(el);
        }
        return false;
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
    }

    private void OnTrayExit(object sender, RoutedEventArgs e)
    {
        TrayIcon.Unregister();
        Application.Current.Shutdown();
    }

    private void OnMinimizeToTray(object sender, RoutedEventArgs e) => HideToTray();
}
