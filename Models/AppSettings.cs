using CommunityToolkit.Mvvm.ComponentModel;

namespace Go2HDR.Models;

public partial class CurvePoint : ObservableObject
{
    [ObservableProperty] private double _brightness;
    [ObservableProperty] private double _sdrValue;

    public int Nits => 80 + (int)SdrValue * 4;

    partial void OnSdrValueChanged(double value) => OnPropertyChanged(nameof(Nits));

    public CurvePoint() { }
    public CurvePoint(double brightness, double sdrValue) { _brightness = brightness; _sdrValue = sdrValue; }
}

public class AppSettings
{
    public List<CurvePoint> CurvePoints { get; set; } = DefaultCurve();
    public int PollIntervalMs { get; set; } = 2000;
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public string Theme { get; set; } = "System";
    public int MinimumBrightness { get; set; } = 40;
    public bool IsEnabled { get; set; } = true;

    public static List<CurvePoint> DefaultCurve(int minBrightness = 40)
    {
        var pts = new List<CurvePoint> { new(minBrightness, 0) };
        foreach (var (b, s) in new (int, double)[] { (60, 10), (70, 22), (80, 42), (90, 64) })
            if (b > minBrightness) pts.Add(new(b, s));
        pts.Add(new(100, 100));
        return pts;
    }
}
