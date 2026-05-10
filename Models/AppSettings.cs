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
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public string Theme { get; set; } = "System";
    public const int DefaultMinimumBrightness = 47;
    public int MinimumBrightness { get; set; } = DefaultMinimumBrightness;
    public bool IsEnabled { get; set; } = true;
    public bool CheckUpdatesOnStartup { get; set; } = true;

    public static List<CurvePoint> DefaultCurve(int minBrightness = DefaultMinimumBrightness)
    {
        (int b, double s)[] curve =
        [
            (47, 0),  (48, 1),  (49, 2),  (50, 3),  (51, 4),
            (52, 5),  (53, 6),  (54, 7),  (55, 8),  (56, 9),
            (57, 10), (58, 12), (59, 13), (60, 14), (61, 15),
            (62, 17), (63, 18), (64, 20), (65, 21), (66, 23),
            (67, 25), (68, 27), (69, 28), (70, 30), (71, 32),
            (72, 34), (73, 35), (74, 37), (75, 39), (76, 41),
            (77, 43), (78, 45), (79, 47), (80, 49), (81, 51),
            (82, 53), (83, 55), (84, 58), (85, 60), (86, 62),
            (87, 65), (88, 67), (89, 69), (90, 72), (91, 75),
            (92, 77), (93, 80), (94, 83), (95, 85), (96, 88),
            (97, 91), (98, 94), (99, 97), (100, 100)
        ];
        var pts = new List<CurvePoint> { new(minBrightness, 0) };
        foreach (var (b, s) in curve)
            if (b > minBrightness) pts.Add(new(b, s));
        return pts;
    }
}
