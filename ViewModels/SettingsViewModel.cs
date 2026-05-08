using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Go2HDR.Services;
using Wpf.Ui.Appearance;

namespace Go2HDR.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly AutostartService _autostart;
    private readonly HdrService _hdrService;

    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private int _pollIntervalMs;
    [ObservableProperty] private int _selectedThemeIndex;
    [ObservableProperty] private bool _autostartInstalled;

    public string[] Themes { get; } = ["System", "Light", "Dark"];

    public SettingsViewModel(SettingsService settings, AutostartService autostart, HdrService hdrService)
    {
        _settings = settings;
        _autostart = autostart;
        _hdrService = hdrService;

        _minimizeToTray = settings.Current.MinimizeToTray;
        _startMinimized = settings.Current.StartMinimized;
        _pollIntervalMs = settings.Current.PollIntervalMs;
        _selectedThemeIndex = settings.Current.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        _autostartInstalled = autostart.IsInstalled();
    }

    partial void OnMinimizeToTrayChanged(bool value) => Save();
    partial void OnStartMinimizedChanged(bool value) => Save();
    partial void OnPollIntervalMsChanged(int value) { Save(); _hdrService.UpdatePollInterval(); }

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
            "Dark" => ApplicationTheme.Dark,
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

    private void Save()
    {
        _settings.Current.MinimizeToTray = MinimizeToTray;
        _settings.Current.StartMinimized = StartMinimized;
        _settings.Current.PollIntervalMs = Math.Clamp(PollIntervalMs, 500, 30000);
        _settings.Save();
    }
}
