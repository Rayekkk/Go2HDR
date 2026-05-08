using CommunityToolkit.Mvvm.ComponentModel;
using Go2HDR.Models;
using Go2HDR.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace Go2HDR.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly HdrService      _hdr;
    private readonly SettingsService _settings;
    private readonly SdrCurveViewModel _curve;

    [ObservableProperty] private bool _isHdrActive;
    [ObservableProperty] private byte _currentBrightness;
    [ObservableProperty] private int  _currentSdrValue;
    [ObservableProperty] private int  _currentNits = 80;

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
            if (value && IsHdrActive)
                _hdr.RefreshSdr();
        }
    }

    public string HdrStatusLabel    => IsHdrActive ? "Active" : "Inactive";
    public string BrightnessText    => IsHdrActive ? $"{CurrentBrightness}%" : "—";
    public string SdrText           => IsHdrActive ? $"{CurrentSdrValue}" : "—";
    public string NitsText          => IsHdrActive ? $"{CurrentNits} nits" : "—";
    public string StatusDescription => !IsEnabled
        ? "Go2HDR is paused — automatic adjustment is disabled."
        : IsHdrActive
            ? "HDR is active — SDR white level is being adjusted automatically."
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

    public ObservableCollection<CurvePoint> CurvePoints  => _curve.CurvePoints;
    public double                           MinBrightness => _curve.MinimumBrightness;

    public CurvePoint? ActivePoint => IsHdrActive
        ? _curve.CurvePoints.FirstOrDefault(p => (int)Math.Round(p.Brightness) == CurrentBrightness)
        : null;

    public DashboardViewModel(HdrService hdr, BrightnessService brightness,
                               SettingsService settings, SdrCurveViewModel curve)
    {
        _hdr      = hdr;
        _settings = settings;
        _curve    = curve;

        hdr.HdrStateChanged   += OnHdrStateChanged;
        hdr.BrightnessChanged += OnBrightnessChanged;

#pragma warning disable MVVMTK0034
        _isHdrActive = hdr.IsHdrActive;
        if (_isHdrActive)
        {
            byte b         = brightness.GetCurrentBrightness();
            _currentBrightness = b;
            _currentSdrValue   = settings.GetSdrValue(b);
            _currentNits       = SettingsService.SdrValueToNits(_currentSdrValue);
        }
#pragma warning restore MVVMTK0034
    }

    partial void OnIsHdrActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(HdrStatusLabel));
        OnPropertyChanged(nameof(BrightnessText));
        OnPropertyChanged(nameof(SdrText));
        OnPropertyChanged(nameof(NitsText));
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

    private void OnBrightnessChanged(byte b) =>
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            CurrentBrightness = b;
            CurrentSdrValue   = _settings.GetSdrValue(b);
            CurrentNits       = SettingsService.SdrValueToNits(CurrentSdrValue);
        });
}
