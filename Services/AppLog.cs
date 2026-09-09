using System.IO;

namespace Go2HDR.Services;

internal static class AppLog
{
    private static readonly object Sync = new();
    private const long MaximumLogBytes = 1_048_576;

    internal static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Go2HDR", "crash.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                RotateIfNeeded();
                string detail = exception is null ? message : $"{message}{Environment.NewLine}{exception}";
                File.AppendAllText(Path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {detail}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never interfere with display control or application shutdown.
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(Path) || new FileInfo(Path).Length < MaximumLogBytes) return;

        string previousPath = Path + ".old";
        File.Move(Path, previousPath, overwrite: true);
    }
}
