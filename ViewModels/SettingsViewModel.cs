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
    [ObservableProperty] private string _autostartStatusText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeAutostart))]
    private bool _isChangingAutostart;

    [ObservableProperty] private bool _checkUpdatesOnStartup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheckForUpdates))]
    private bool _isCheckingUpdate;

    [ObservableProperty] private string _updateStatusText = "";
    [ObservableProperty] private bool _updateAvailable;
    private Uri? _updateUri;
    private bool _settingAutostartState;

    public bool CanCheckForUpdates => !IsCheckingUpdate;
    public bool CanChangeAutostart => !IsChangingAutostart;
    public string AppVersionLabel => $"v{UpdateService.CurrentVersion.ToString(3)}";
    public IReadOnlyList<string> Themes { get; } = ["System", "Light", "Dark"];

    public SettingsViewModel(SettingsService settings, AutostartService autostart,
                              UpdateService updateService)
    {
        _settings = settings;
        _autostart = autostart;
        _updateService = updateService;
        _updateService.CheckCompleted += OnUpdateCheckCompleted;

        _minimizeToTray = settings.Current.MinimizeToTray;
        _startMinimized = settings.Current.StartMinimized;
        _selectedThemeIndex = settings.Current.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        _autostartInstalled = settings.Current.StartWithWindows;
        _autostartStatusText = "Checking autostart status...";
        _checkUpdatesOnStartup = settings.Current.CheckUpdatesOnStartup;

        ApplyLastResult(updateService.LastResult);
        _ = RefreshAutostartAsync();
    }

    partial void OnMinimizeToTrayChanged(bool value) => Save();
    partial void OnStartMinimizedChanged(bool value) => Save();
    partial void OnCheckUpdatesOnStartupChanged(bool value) => Save();

    partial void OnSelectedThemeIndexChanged(int value)
    {
        if ((uint)value >= (uint)Themes.Count) return;
        _settings.Current.Theme = Themes[value];
        Save();
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        ApplicationThemeManager.Apply(_settings.Current.Theme switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark" => ApplicationTheme.Dark,
            _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark
                    ? ApplicationTheme.Dark : ApplicationTheme.Light
        });
    }

    partial void OnAutostartInstalledChanged(bool value)
    {
        if (_settingAutostartState) return;
        _ = ChangeAutostartAsync(value);
    }

    private async Task RefreshAutostartAsync()
    {
        if (IsChangingAutostart) return;
        IsChangingAutostart = true;
        try
        {
            bool? installed = await _autostart.IsInstalledAsync();
            if (installed is null)
            {
                AutostartStatusText = "Couldn't verify autostart. The last saved setting is shown.";
                return;
            }

            SetAutostartState(installed.Value);
            _settings.Current.StartWithWindows = installed.Value;
            _settings.Save();
            AutostartStatusText = GetAutostartStatusText(installed.Value);
        }
        catch (Exception ex)
        {
            AppLog.Write("Refreshing the autostart setting failed.", ex);
            AutostartStatusText = "Couldn't verify autostart. The last saved setting is shown.";
        }
        finally
        {
            IsChangingAutostart = false;
        }
    }

    private async Task ChangeAutostartAsync(bool requested)
    {
        if (IsChangingAutostart) return;
        IsChangingAutostart = true;
        AutostartStatusText = requested ? "Enabling autostart..." : "Disabling autostart...";
        try
        {
            bool commandSucceeded = requested
                ? await _autostart.InstallAsync()
                : await _autostart.RemoveAsync();
            bool? actualState = await _autostart.IsInstalledAsync();
            bool confirmed = actualState == requested || commandSucceeded && actualState is null;

            if (confirmed)
            {
                SetAutostartState(requested);
                _settings.Current.StartWithWindows = requested;
                _settings.Save();
                AutostartStatusText = GetAutostartStatusText(requested);
            }
            else
            {
                bool restoredState = actualState ?? !requested;
                SetAutostartState(restoredState);
                _settings.Current.StartWithWindows = restoredState;
                _settings.Save();
                AutostartStatusText = "Could not change autostart. Details were saved to the application log.";
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Changing the autostart setting failed.", ex);
            SetAutostartState(!requested);
            AutostartStatusText = "Could not change autostart. Details were saved to the application log.";
        }
        finally
        {
            IsChangingAutostart = false;
        }
    }

    private void SetAutostartState(bool value)
    {
        _settingAutostartState = true;
        try { AutostartInstalled = value; }
        finally { _settingAutostartState = false; }
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            IsCheckingUpdate = true;
            UpdateStatusText = "Checking...";
            UpdateAvailable = false;
            UpdateResult? result = await _updateService.CheckAsync();
            if (result is null)
            {
                _updateUri = null;
                UpdateAvailable = false;
                UpdateStatusText = "Couldn't check for updates. Check your internet connection and try again.";
            }
            else
            {
                ApplyLastResult(result);
            }
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (_updateUri is null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _updateUri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Write("Opening the update page failed.", ex);
        }
    }

    private void ApplyLastResult(UpdateResult? result)
    {
        if (result is null)
        {
            UpdateAvailable = false;
            _updateUri = null;
            if (UpdateStatusText == "")
                UpdateStatusText = "Not checked yet";
            return;
        }
        if (result.IsNewer)
        {
            UpdateStatusText = $"Version {result.LatestVersion} is available";
            UpdateAvailable = true;
            _updateUri = result.ReleaseUri;
        }
        else
        {
            UpdateStatusText = "You're up to date";
            UpdateAvailable = false;
            _updateUri = null;
        }
    }

    private static string GetAutostartStatusText(bool installed) => installed
        ? "Go2HDR will start automatically at login."
        : "Start Go2HDR automatically when you log into Windows.";

    private void OnUpdateCheckCompleted(UpdateResult? result) =>
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // The manual command owns its status while it is running. This event keeps
            // Settings in sync with the automatic startup check.
            if (IsCheckingUpdate) return;
            if (result is null)
            {
                UpdateAvailable = false;
                _updateUri = null;
                UpdateStatusText = "Couldn't check for updates. Check your internet connection and try again.";
            }
            else
            {
                ApplyLastResult(result);
            }
        });

    private void Save()
    {
        _settings.Current.MinimizeToTray = MinimizeToTray;
        _settings.Current.StartMinimized = StartMinimized;
        _settings.Current.StartWithWindows = AutostartInstalled;
        _settings.Current.CheckUpdatesOnStartup = CheckUpdatesOnStartup;
        _settings.Save();
    }
}
