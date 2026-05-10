using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Go2HDR.Services;
using Wpf.Ui.Appearance;

namespace Go2HDR.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly AutostartService _autostart;
    private readonly UpdateService _updateService;

    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private int _selectedThemeIndex;
    [ObservableProperty] private bool _autostartInstalled;
    [ObservableProperty] private bool _checkUpdatesOnStartup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheckForUpdates))]
    private bool _isCheckingUpdate;

    [ObservableProperty] private string _updateStatusText = "";
    [ObservableProperty] private bool _updateAvailable;
    private string? _updateUrl;

    public bool CanCheckForUpdates => !IsCheckingUpdate;
    public string AppVersionLabel => $"v{UpdateService.CurrentVersion.ToString(3)}";
    public string[] Themes { get; } = ["System", "Light", "Dark"];

    public SettingsViewModel(SettingsService settings, AutostartService autostart,
                              UpdateService updateService)
    {
        _settings = settings;
        _autostart = autostart;
        _updateService = updateService;

        _minimizeToTray        = settings.Current.MinimizeToTray;
        _startMinimized        = settings.Current.StartMinimized;
        _selectedThemeIndex    = settings.Current.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        _autostartInstalled    = autostart.IsInstalled();
        _checkUpdatesOnStartup = settings.Current.CheckUpdatesOnStartup;

        ApplyLastResult(updateService.LastResult);
    }

    partial void OnMinimizeToTrayChanged(bool value) => Save();
    partial void OnStartMinimizedChanged(bool value) => Save();
    partial void OnCheckUpdatesOnStartupChanged(bool value) => Save();

    partial void OnSelectedThemeIndexChanged(int value)
    {
        _settings.Current.Theme = Themes[value];
        Save();
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        ApplicationThemeManager.Apply(_settings.Current.Theme switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark"  => ApplicationTheme.Dark,
            _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
                    ? ApplicationTheme.Dark : ApplicationTheme.Light
        });
    }

    partial void OnAutostartInstalledChanged(bool value)
    {
        bool ok = value ? _autostart.Install() : _autostart.Remove();
        if (!ok)
        {
            _autostartInstalled = !value;
            OnPropertyChanged(nameof(AutostartInstalled));
        }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingUpdate = true;
        UpdateStatusText = "Checking...";
        UpdateAvailable  = false;

        var result = await _updateService.CheckAsync();

        IsCheckingUpdate = false;
        ApplyLastResult(result);
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (_updateUrl is null) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = _updateUrl,
            UseShellExecute = true
        });
    }

    private void ApplyLastResult(UpdateResult? result)
    {
        if (result is null)
        {
            if (UpdateStatusText is "" or "Checking...")
                UpdateStatusText = "Not checked yet";
            return;
        }
        if (result.IsNewer)
        {
            UpdateStatusText = $"Version {result.LatestVersion} is available";
            UpdateAvailable  = true;
            _updateUrl       = result.ReleaseUrl;
        }
        else
        {
            UpdateStatusText = "You're up to date";
        }
    }

    private void Save()
    {
        _settings.Current.MinimizeToTray        = MinimizeToTray;
        _settings.Current.StartMinimized        = StartMinimized;
        _settings.Current.CheckUpdatesOnStartup = CheckUpdatesOnStartup;
        _settings.Save();
    }
}
