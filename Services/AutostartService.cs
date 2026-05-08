using Microsoft.Win32;
using System.Diagnostics;

namespace Go2HDR.Services;

public class AutostartService
{
    private const string RunKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Go2HDR";

    private static string ExeEntry =>
        $"\"{Process.GetCurrentProcess().MainModule!.FileName}\"";

    public bool IsInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) != null;
        }
        catch { return false; }
    }

    public bool Install()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)!;
            key.SetValue(AppName, ExeEntry);
            return true;
        }
        catch { return false; }
    }

    public bool Remove()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)!;
            key.DeleteValue(AppName, throwOnMissingValue: false);
            return true;
        }
        catch { return false; }
    }

    // Called at startup: keeps the registry path in sync when the exe moves (e.g. after update).
    public void SyncPath()
    {
        if (!IsInstalled()) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(AppName) as string != ExeEntry)
                Install();
        }
        catch { }
    }
}
