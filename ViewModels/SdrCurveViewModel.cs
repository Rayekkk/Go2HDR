using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Go2HDR.Models;
using Go2HDR.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
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
        // Set backing fields directly to skip OnMinimumBrightnessChanged side-effects;
        // LoadFromSettings() will rebuild CurvePoints from scratch anyway.
#pragma warning disable MVVMTK0034
        _minimumBrightness    = AppSettings.DefaultMinimumBrightness;
        _pendingMinBrightness = AppSettings.DefaultMinimumBrightness;
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(MinimumBrightness));
        OnPropertyChanged(nameof(PendingMinBrightness));
        _settings.Current.MinimumBrightness = AppSettings.DefaultMinimumBrightness;
        _settings.Current.CurvePoints = AppSettings.DefaultCurve();
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

    [RelayCommand]
    public void ExportCurve()
    {
        var dialog = new SaveFileDialog
        {
            Title      = "Export SDR Curve",
            Filter     = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            FileName   = "sdr_curve.json"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var pts = CurvePoints.OrderBy(p => p.Brightness)
                .Select(p => new { brightness = (int)Math.Round(p.Brightness), sdrValue = p.SdrValue });
            var json = JsonSerializer.Serialize(pts, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dialog.FileName, json);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to export curve:\n{ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void ImportCurve()
    {
        var dialog = new OpenFileDialog
        {
            Title      = "Import SDR Curve",
            Filter     = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2)
                throw new FormatException("File must contain a JSON array with at least 2 points.");

            var imported = new List<(int brightness, double sdrValue)>();
            foreach (var el in root.EnumerateArray())
            {
                int    b = (int)Math.Round(el.GetProperty("brightness").GetDouble());
                double s = Math.Clamp(el.GetProperty("sdrValue").GetDouble(), 0, 100);
                b = Math.Clamp(b, 1, 100);
                imported.Add((b, s));
            }

            int minB = imported.Min(p => p.brightness);

            foreach (var old in CurvePoints) old.PropertyChanged -= OnPointPropertyChanged;
            CurvePoints.Clear();

            foreach (var (b, s) in imported.OrderBy(p => p.brightness))
            {
                var pt = new CurvePoint(b, s);
                pt.PropertyChanged += OnPointPropertyChanged;
                CurvePoints.Add(pt);
            }

#pragma warning disable MVVMTK0034
            _minimumBrightness    = minB;
            _pendingMinBrightness = minB;
#pragma warning restore MVVMTK0034
            OnPropertyChanged(nameof(MinimumBrightness));
            OnPropertyChanged(nameof(PendingMinBrightness));
            _settings.Current.MinimumBrightness = minB;

            SaveCurve();
            SelectedPoint = null;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to import curve:\n{ex.Message}", "Import Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
