using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Go2HDR.Models;
using Go2HDR.Services;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace Go2HDR.ViewModels;

public partial class SdrCurveViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly DispatcherTimer _saveDebounce;

    public ObservableCollection<CurvePoint> CurvePoints { get; } = [];

    [ObservableProperty] private CurvePoint? _selectedPoint;
    [ObservableProperty] private int _minimumBrightness;
    [ObservableProperty] private int _pendingMinBrightness;

    public SdrCurveViewModel(SettingsService settings)
    {
        _settings = settings;
        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); SaveCurve(); };
#pragma warning disable MVVMTK0034
        _minimumBrightness    = _settings.Current.MinimumBrightness;
        _pendingMinBrightness = _settings.Current.MinimumBrightness;
#pragma warning restore MVVMTK0034
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        foreach (var old in CurvePoints) old.PropertyChanged -= OnPointPropertyChanged;
        CurvePoints.Clear();

        for (int b = MinimumBrightness; b <= 100; b++)
        {
            var pt = new CurvePoint(b, _settings.GetSdrValue((byte)b));
            pt.PropertyChanged += OnPointPropertyChanged;
            CurvePoints.Add(pt);
        }
    }

    private void OnPointPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    partial void OnMinimumBrightnessChanged(int value)
    {
        value = Math.Clamp(value, 1, 99);

        var below = CurvePoints.Where(p => p.Brightness < value).ToList();
        foreach (var p in below) { p.PropertyChanged -= OnPointPropertyChanged; CurvePoints.Remove(p); }

        var taken = CurvePoints.Select(p => (int)Math.Round(p.Brightness)).ToHashSet();
        for (int b = value; b <= 100; b++)
        {
            if (!taken.Contains(b))
            {
                var pt = new CurvePoint(b, _settings.GetSdrValue((byte)b));
                pt.PropertyChanged += OnPointPropertyChanged;
                CurvePoints.Add(pt);
            }
        }

        SelectedPoint = null;
        PendingMinBrightness = value;
        _settings.Current.MinimumBrightness = value;
        SaveCurve();
    }

    [RelayCommand]
    public void ApplyMinBrightness() => MinimumBrightness = PendingMinBrightness;

    [RelayCommand]
    public void SaveCurve()
    {
        _saveDebounce.Stop();
        _settings.Current.CurvePoints = CurvePoints.OrderBy(p => p.Brightness)
            .Select(p => new CurvePoint(p.Brightness, p.SdrValue)).ToList();
        _settings.Save();
    }

    [RelayCommand]
    public void ResetToDefault()
    {
        _settings.Current.CurvePoints = AppSettings.DefaultCurve(MinimumBrightness);
        LoadFromSettings();
        SaveCurve();
        SelectedPoint = null;
    }

    [RelayCommand]
    public void SetAllTo100()
    {
        foreach (var pt in CurvePoints)
            pt.SdrValue = 100;
        SaveCurve();
    }
}
