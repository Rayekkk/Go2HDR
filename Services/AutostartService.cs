using Microsoft.Win32;
using System.Diagnostics;
using System.Text;

namespace Go2HDR.Services;

// Task Scheduler is used instead of a registry Run key because Xbox FSE (Full Screen
// Experience) intentionally delays all HKCU\Run entries until the user first opens the
// desktop — meaning Go2HDR would never start in Xbox Mode.  Task Scheduler "At logon"
// tasks are dispatched by the Schedule service before FSE applies its startup filter.
//
// PowerShell Register-ScheduledTask is used instead of schtasks.exe because
// "schtasks /create /sc onlogon" without an explicit /ru flag fails on Windows 11
// (requires elevation or prompts for credentials), causing the toggle to bounce back.
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
        const string script = """
            $t = Get-ScheduledTask -TaskName 'Go2HDR' -ErrorAction SilentlyContinue
            if ($t) { exit 0 } else { exit 1 }
            """;
        return RunPowerShell(script);
    }

    private static bool CreateTask()
    {
        var exe = ExePath.Replace("'", "''"); // escape single quotes for PowerShell
        var script = $$"""
            try {
                $action   = New-ScheduledTaskAction -Execute '{{exe}}'
                $trigger  = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
                $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
                Register-ScheduledTask -TaskName 'Go2HDR' -Action $action -Trigger $trigger -Settings $settings -RunLevel Limited -Force -ErrorAction Stop | Out-Null
                exit 0
            } catch {
                exit 1
            }
            """;
        return RunPowerShell(script);
    }

    private static bool DeleteTask()
    {
        const string script = """
            Unregister-ScheduledTask -TaskName 'Go2HDR' -Confirm:$false -ErrorAction SilentlyContinue
            exit 0
            """;
        return RunPowerShell(script);
    }

    private static bool RunPowerShell(string script)
    {
        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new ProcessStartInfo("powershell.exe")
            {
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-WindowStyle");
            psi.ArgumentList.Add("Hidden");
            psi.ArgumentList.Add("-EncodedCommand");
            psi.ArgumentList.Add(encoded);
            using var p = Process.Start(psi)!;
            if (!p.WaitForExit(10_000))
            {
                p.Kill();
                return false;
            }
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
}
