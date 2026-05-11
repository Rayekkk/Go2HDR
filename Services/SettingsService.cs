using Go2HDR.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Go2HDR.Services;

public class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Go2HDR", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public AppSettings Current { get; private set; } = new();

    // Sorted snapshot — rebuilt lazily after every Load/Save, used by hot-path GetSdrValue.
    // volatile so writes from Save() (UI thread) are immediately visible to WMI thread.
    private volatile CurvePoint[]? _sortedPoints;

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                if (Current.CurvePoints.Count == 0)
                    Current.CurvePoints = AppSettings.DefaultCurve();
            }
        }
        catch { Current = new AppSettings(); }
        _sortedPoints = null;
    }

    public event Action? Saved;

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch { }
        _sortedPoints = null;
        Saved?.Invoke();
    }

    public int GetSdrValue(byte brightness)
    {
        if (brightness < Current.MinimumBrightness) return 0;
        var pts = _sortedPoints ??= [.. Current.CurvePoints.OrderBy(p => p.Brightness)];
        if (pts.Length == 0) return 0;
        if (brightness <= pts[0].Brightness) return (int)pts[0].SdrValue;
        if (brightness >= pts[^1].Brightness) return (int)pts[^1].SdrValue;

        for (int i = 1; i < pts.Length; i++)
        {
            if (brightness <= pts[i].Brightness)
            {
                var p0 = pts[i - 1];
                var p1 = pts[i];
                double t = (brightness - p0.Brightness) / (p1.Brightness - p0.Brightness);
                return (int)Math.Round(p0.SdrValue + t * (p1.SdrValue - p0.SdrValue));
            }
        }
        return (int)pts[^1].SdrValue;
    }

    public static int SdrValueToNits(int sdrValue) => 80 + sdrValue * 4;
}
