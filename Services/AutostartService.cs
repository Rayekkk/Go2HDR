using Microsoft.Win32;
using System.Diagnostics;

namespace Go2HDR.Services;

// Task Scheduler is used instead of a registry Run key because Xbox FSE (Full Screen
// Experience) intentionally delays all HKCU\Run entries until the user first opens the
// desktop — meaning Go2HDR would never start in Xbox Mode.  Task Scheduler "At logon"
// tasks are dispatched by the Schedule service before FSE applies its startup filter.
public class AutostartService
{
    private const string RunKey   = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string TaskName = "Go2HDR";
    private const string AppName  = "Go2HDR";

    private static string ExePath =>
        Process.GetCurrentProcess().MainModule!.FileName;

    public bool IsInstalled() => IsTaskInstalled() || IsRegistryInstalled();

    public bool Install()
    {
        RemoveRegistry(); // clear legacy registry entry if present
        return CreateTask();
    }

    public bool Remove()
    {
        RemoveRegistry();
        return DeleteTask();
    }

    // Migrates legacy registry autostart to Task Scheduler on first run after update.
    // Fast no-op for users already on Task Scheduler.
    public void SyncPath()
    {
        if (!IsRegistryInstalled()) return;
        Install(); // replace registry entry with a task
    }

    // ── Task Scheduler ────────────────────────────────────────────────────────

    private static bool IsTaskInstalled()
    {
        try
        {
            using var p = Process.Start(Schtasks("/query", "/tn", TaskName, "/fo", "list"))!;
            p.WaitForExit(3000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static bool CreateTask()
    {
        try
        {
            // /it         = run only in the user's interactive session
            // /rl limited = standard (non-elevated) privileges
            // /f          = overwrite existing task with the same name
            using var p = Process.Start(
                Schtasks("/create", "/tn", TaskName, "/tr", ExePath,
                         "/sc", "onlogon", "/it", "/rl", "limited", "/f"))!;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static bool DeleteTask()
    {
        try
        {
            using var p = Process.Start(Schtasks("/delete", "/tn", TaskName, "/f"))!;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    // ── Registry (legacy) ─────────────────────────────────────────────────────

    private static bool IsRegistryInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) != null;
        }
        catch { return false; }
    }

    private static void RemoveRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ProcessStartInfo Schtasks(params string[] args)
    {
        var psi = new ProcessStartInfo("schtasks")
        {
            CreateNoWindow         = true,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }
}
