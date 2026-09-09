using Go2HDR.Services;
using Go2HDR.ViewModels;
using Go2HDR.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace Go2HDR;

public partial class App : Application
{
    private static Mutex? _mutex;
    private static bool _ownsMutex;
    private ServiceProvider? _serviceProvider;
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException +=
            (_, ex) => AppLog.Write($"UnhandledException: {ex.ExceptionObject}");
        DispatcherUnhandledException +=
            (_, ex) => AppLog.Write("DispatcherUnhandledException", ex.Exception);

        try
        {
            StartupCore(e);
        }
        catch (Exception ex)
        {
            AppLog.Write("Startup failed.", ex);
            MessageBox.Show($"Go2HDR failed to start.\n\nDetails saved to:\n{AppLog.Path}",
                "Go2HDR", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void StartupCore(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "Go2HDR_SingleInstance", out bool isNew);
        if (!isNew) { Shutdown(); return; }
        _ownsMutex = true;

        var sc = new ServiceCollection();
        sc.AddSingleton<SettingsService>();
        sc.AddSingleton<DisplayConfigService>();
        sc.AddSingleton<BrightnessService>();
        sc.AddSingleton<AutostartService>();
        sc.AddSingleton<UpdateService>();
        sc.AddSingleton<NotificationService>();
        sc.AddSingleton<HdrService>();
        sc.AddSingleton<DashboardViewModel>();
        sc.AddSingleton<SdrCurveViewModel>();
        sc.AddSingleton<SettingsViewModel>();
        sc.AddSingleton<DashboardPage>();
        sc.AddSingleton<SdrCurvePage>();
        sc.AddSingleton<SettingsPage>();
        sc.AddSingleton<MainWindow>();
        _serviceProvider = sc.BuildServiceProvider();
        Services = _serviceProvider;

        var settings = Services.GetRequiredService<SettingsService>();
        settings.Load();

        ApplyTheme(settings.Current.Theme);

        _ = Services.GetRequiredService<AutostartService>().SyncPathAsync();

        var hdr = Services.GetRequiredService<HdrService>();
        hdr.Start();

        if (settings.Current.CheckUpdatesOnStartup)
        {
            var notifySvc = Services.GetRequiredService<NotificationService>();
            var updateSvc = Services.GetRequiredService<UpdateService>();
            updateSvc.NewVersionFound += r => notifySvc.ShowUpdateAvailable(r.LatestVersion, r.ReleaseUri);
            _ = updateSvc.CheckAsync();
        }

        var window = Services.GetRequiredService<MainWindow>();

        if (settings.Current.StartMinimized)
        {
            window.ShowInTaskbar = false;
            window.WindowState = WindowState.Minimized;
            window.Show();
            window.Hide();
        }
        else
        {
            window.Show();
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        _serviceProvider = null;
        if (_ownsMutex && _mutex is not null)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }
        _mutex?.Dispose();
        _mutex = null;
        base.OnExit(e);
    }

    private static void ApplyTheme(string theme)
    {
        ApplicationThemeManager.Apply(theme switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark" => ApplicationTheme.Dark,
            _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
                    ? ApplicationTheme.Dark : ApplicationTheme.Light
        });
    }
}
