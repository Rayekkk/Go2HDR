using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Go2HDR.Models;
using Go2HDR.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;

namespace Go2HDR.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly HdrService _hdr;
    private readonly SettingsService _settings;
    private readonly SdrCurveViewModel _curve;

    [ObservableProperty] private bool _isHdrActive;
    [ObservableProperty] private bool _isHdrStateAvailable;
    [ObservableProperty] private bool _hasBrightnessReading;
    [ObservableProperty] private byte _currentBrightness;
    [ObservableProperty] private int _currentSdrValue;
    [ObservableProperty] private int _currentNits = 80;
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _updateVersion = "";
    private Uri? _updateUri;

    public bool IsEnabled
    {
        get => _settings.Current.IsEnabled;
        set
        {
            if (_settings.Current.IsEnabled == value) return;
            _settings.Current.IsEnabled = value;
            _settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusDescription));
        }
    }

    public BuiltInHdrState HdrVisualState => !IsHdrStateAvailable
        ? BuiltInHdrState.Unavailable
        : IsHdrActive ? BuiltInHdrState.Active : BuiltInHdrState.Inactive;
    public bool HasLiveReading => IsHdrStateAvailable && IsHdrActive && HasBrightnessReading;
    public string HdrStatusLabel => HdrVisualState switch
    {
        BuiltInHdrState.Active => "Active",
        BuiltInHdrState.Inactive => "Inactive",
        _ => "Unavailable"
    };
    public string BrightnessText => HasLiveReading ? $"{CurrentBrightness}%" : "—";
    public string SdrText => HasLiveReading ? $"{CurrentSdrValue}" : "—";
    public string NitsText => HasLiveReading ? $"{CurrentNits} nits" : "—";
    public string StatusDescription => !IsHdrStateAvailable
        ? "The HDR display state is temporarily unavailable — Go2HDR will retry automatically."
        : !IsEnabled
        ? "Go2HDR is paused — automatic adjustment is disabled."
        : IsHdrActive
            ? HasBrightnessReading
                ? "HDR is active — SDR white level is being adjusted automatically."
                : "HDR is active, but screen brightness is temporarily unavailable."
            : "HDR is not active — waiting for HDR to be enabled on the display.";

    public string SdrRangeText
    {
        get
        {
            var pts = _settings.Current.CurvePoints;
            if (pts.Count == 0) return "—";
            int lo = SettingsService.SdrValueToNits((int)pts.Min(p => p.SdrValue));
            int hi = SettingsService.SdrValueToNits((int)pts.Max(p => p.SdrValue));
            return $"{lo} – {hi} nits";
        }
    }

    public ObservableCollection<CurvePoint> CurvePoints => _curve.CurvePoints;
    public double MinBrightness => _curve.MinimumBrightness;

    public CurvePoint? ActivePoint => HasLiveReading
        ? _curve.CurvePoints.FirstOrDefault(p => (int)Math.Round(p.Brightness) == CurrentBrightness)
        : null;

    public DashboardViewModel(HdrService hdr, BrightnessService brightness,
                               SettingsService settings, SdrCurveViewModel curve,
                               UpdateService update)
    {
        _hdr = hdr;
        _settings = settings;
        _curve = curve;

        hdr.HdrStateChanged += OnHdrStateChanged;
        hdr.HdrAvailabilityChanged += OnHdrAvailabilityChanged;
        hdr.BrightnessChanged += OnBrightnessChanged;
        settings.Changed += OnSettingsChanged;

        update.NewVersionFound += r => Application.Current.Dispatcher.InvokeAsync(() =>
        {
            UpdateAvailable = true;
            UpdateVersion = r.LatestVersion;
            _updateUri = r.ReleaseUri;
        });

#pragma warning disable MVVMTK0034
        _isHdrActive = hdr.IsHdrActive;
        _isHdrStateAvailable = hdr.IsStateAvailable;
        if (_isHdrActive)
        {
            if (brightness.TryGetCurrentBrightness(out byte b))
            {
                _hasBrightnessReading = true;
                _currentBrightness = b;
                _currentSdrValue = settings.GetSdrValue(b);
                _currentNits = SettingsService.SdrValueToNits(_currentSdrValue);
            }
        }

        // Catch the case where CheckAsync ran and completed before this VM was constructed.
        if (update.LastResult?.IsNewer == true)
        {
            _updateAvailable = true;
            _updateVersion = update.LastResult.LatestVersion;
            _updateUri = update.LastResult.ReleaseUri;
        }
#pragma warning restore MVVMTK0034
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (_updateUri is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(_updateUri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLog.Write("Opening the update page failed.", ex);
            }
        }
    }

    partial void OnIsHdrActiveChanged(bool value)
    {
        HasBrightnessReading = false;
        OnPropertyChanged(nameof(HdrStatusLabel));
        OnPropertyChanged(nameof(HdrVisualState));
        OnPropertyChanged(nameof(HasLiveReading));
        OnPropertyChanged(nameof(BrightnessText));
        OnPropertyChanged(nameof(SdrText));
        OnPropertyChanged(nameof(NitsText));
        OnPropertyChanged(nameof(StatusDescription));
        OnPropertyChanged(nameof(ActivePoint));
    }

    partial void OnIsHdrStateAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(HdrStatusLabel));
        OnPropertyChanged(nameof(HdrVisualState));
        OnPropertyChanged(nameof(HasLiveReading));
        OnPropertyChanged(nameof(BrightnessText));
        OnPropertyChanged(nameof(SdrText));
        OnPropertyChanged(nameof(NitsText));
        OnPropertyChanged(nameof(StatusDescription));
        OnPropertyChanged(nameof(ActivePoint));
    }

    partial void OnHasBrightnessReadingChanged(bool value)
    {
        OnPropertyChanged(nameof(BrightnessText));
        OnPropertyChanged(nameof(SdrText));
        OnPropertyChanged(nameof(NitsText));
        OnPropertyChanged(nameof(HasLiveReading));
        OnPropertyChanged(nameof(StatusDescription));
        OnPropertyChanged(nameof(ActivePoint));
    }

    partial void OnCurrentBrightnessChanged(byte value)
    {
        OnPropertyChanged(nameof(BrightnessText));
        OnPropertyChanged(nameof(ActivePoint));
    }

    partial void OnCurrentSdrValueChanged(int value)
    {
        OnPropertyChanged(nameof(SdrText));
        OnPropertyChanged(nameof(NitsText));
    }

    partial void OnCurrentNitsChanged(int value) => OnPropertyChanged(nameof(NitsText));

    private void OnHdrStateChanged(bool active) =>
        Application.Current.Dispatcher.InvokeAsync(() => IsHdrActive = active);

    private void OnHdrAvailabilityChanged(bool available) =>
        Application.Current.Dispatcher.InvokeAsync(() => IsHdrStateAvailable = available);

    private void OnBrightnessChanged(byte b) =>
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            HasBrightnessReading = true;
            CurrentBrightness = b;
            CurrentSdrValue = _settings.GetSdrValue(b);
            CurrentNits = SettingsService.SdrValueToNits(CurrentSdrValue);
        });

    // Refresh derived properties that read from settings — called whenever settings are saved
    // (curve edits, MinimumBrightness change, reset). Keeps the Dashboard curve card in sync
    // with changes made on SdrCurvePage without requiring a page reload.
    private void OnSettingsChanged() =>
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(SdrRangeText));
            OnPropertyChanged(nameof(MinBrightness));
        });
}
