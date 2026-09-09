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
            }
            Normalize(Current);
        }
        catch (Exception ex)
        {
            AppLog.Write("Loading settings failed; defaults will be used.", ex);
            PreserveInvalidSettingsFile();
            Current = new AppSettings();
        }
        _sortedPoints = null;
    }

    public event Action? Changed;

    public bool Save()
    {
        string temporaryPath = SettingsPath + ".tmp";
        bool saved = false;
        try
        {
            Normalize(Current);
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Current, JsonOptions));
            if (File.Exists(SettingsPath))
                File.Replace(temporaryPath, SettingsPath, null);
            else
                File.Move(temporaryPath, SettingsPath);
            saved = true;
        }
        catch (Exception ex)
        {
            AppLog.Write("Saving settings failed.", ex);
            try { File.Delete(temporaryPath); }
            catch { }
        }
        _sortedPoints = null;
        Changed?.Invoke();
        return saved;
    }

    public int GetSdrValue(byte brightness)
    {
        if (brightness < Current.MinimumBrightness) return 0;
        var pts = _sortedPoints ??= [.. Current.CurvePoints.OrderBy(p => p.Brightness)];
        if (pts.Length == 0) return 0;
        if (brightness <= pts[0].Brightness) return (int)Math.Round(pts[0].SdrValue);
        if (brightness >= pts[^1].Brightness) return (int)Math.Round(pts[^1].SdrValue);

        for (int i = 1; i < pts.Length; i++)
        {
            if (brightness <= pts[i].Brightness)
            {
                var p0 = pts[i - 1];
                var p1 = pts[i];
                double t = (brightness - p0.Brightness) / (p1.Brightness - p0.Brightness);
                return Math.Clamp((int)Math.Round(p0.SdrValue + t * (p1.SdrValue - p0.SdrValue)), 0, 100);
            }
        }
        return Math.Clamp((int)Math.Round(pts[^1].SdrValue), 0, 100);
    }

    public static int SdrValueToNits(int sdrValue) => 80 + sdrValue * 4;

    internal static void Normalize(AppSettings settings)
    {
        settings.MinimumBrightness = Math.Clamp(settings.MinimumBrightness,
            AppSettings.MinimumAllowedBrightness, AppSettings.MaximumMinimumBrightness);
        if (settings.Theme is not ("System" or "Light" or "Dark"))
            settings.Theme = "System";

        var points = (settings.CurvePoints ?? [])
            .Where(p => double.IsFinite(p.Brightness) && double.IsFinite(p.SdrValue))
            .Select(p => new CurvePoint(
                Math.Clamp(Math.Round(p.Brightness), settings.MinimumBrightness, 100),
                Math.Clamp(Math.Round(p.SdrValue), 0, 100)))
            .GroupBy(p => p.Brightness)
            .Select(g => g.Last())
            .OrderBy(p => p.Brightness)
            .ToList();

        if (points.Count == 0)
        {
            settings.CurvePoints = AppSettings.DefaultCurve(settings.MinimumBrightness);
            return;
        }

        if (points[0].Brightness > settings.MinimumBrightness)
            points.Insert(0, new CurvePoint(settings.MinimumBrightness, points[0].SdrValue));
        if (points[^1].Brightness < 100)
            points.Add(new CurvePoint(100, points[^1].SdrValue));

        settings.CurvePoints = points;
    }

    private static void PreserveInvalidSettingsFile()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            string directory = Path.GetDirectoryName(SettingsPath)!;
            string backup = Path.Combine(directory,
                $"settings.invalid-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(SettingsPath, backup, overwrite: false);
        }
        catch (Exception ex)
        {
            AppLog.Write("Preserving the invalid settings file failed.", ex);
        }
    }
}
