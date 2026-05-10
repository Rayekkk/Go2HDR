using Go2HDR.Services;
using Go2HDR.ViewModels;
using Go2HDR.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace Go2HDR;

public partial class App : Application
{
    private static Mutex? _mutex;
    public static IServiceProvider Services { get; private set; } = null!;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Go2HDR", "crash.log");

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException +=
            (_, ex) => Log($"UnhandledException: {ex.ExceptionObject}");
        DispatcherUnhandledException +=
            (_, ex) => { Log($"DispatcherUnhandledException: {ex.Exception}"); ex.Handled = true; };

        try
        {
            StartupCore(e);
        }
        catch (Exception ex)
        {
            Log($"Startup failed: {ex}");
            MessageBox.Show($"Go2HDR failed to start.\n\nDetails saved to:\n{LogPath}",
                "Go2HDR", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void StartupCore(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "Go2HDR_SingleInstance", out bool isNew);
        if (!isNew) { Shutdown(); return; }

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
        Services = sc.BuildServiceProvider();

        var settings  = Services.GetRequiredService<SettingsService>();
        settings.Load();

        ApplyTheme(settings.Current.Theme);

        // Keep autostart registry entry pointing to the current exe (handles in-place updates).
        Services.GetRequiredService<AutostartService>().SyncPath();

        var hdr = Services.GetRequiredService<HdrService>();
        hdr.Start();

        if (settings.Current.CheckUpdatesOnStartup)
        {
            var notifySvc = Services.GetRequiredService<NotificationService>();
            var updateSvc = Services.GetRequiredService<UpdateService>();
            updateSvc.NewVersionFound += r => notifySvc.ShowUpdateAvailable(r.LatestVersion, r.ReleaseUrl);
            _ = updateSvc.CheckAsync();
        }

        var window = Services.GetRequiredService<MainWindow>();

        if (settings.Current.StartMinimized)
        {
            // Show briefly so the window handle and tray icon are initialised, then hide.
            window.ShowInTaskbar = false;
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
        Services.GetRequiredService<HdrService>().Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private static void ApplyTheme(string theme)
    {
        ApplicationThemeManager.Apply(theme switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark"  => ApplicationTheme.Dark,
            _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
                    ? ApplicationTheme.Dark : ApplicationTheme.Light
        });
    }
}
