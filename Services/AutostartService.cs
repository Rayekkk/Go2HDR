using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Go2HDR.Services;

// Task Scheduler is used instead of a registry Run key because Xbox FSE (Full Screen
// Experience) can delay HKCU\Run entries until the desktop is opened. A per-user,
// limited scheduled task starts before that filter is applied and does not require UAC.
public sealed class AutostartService
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string TaskName = "Go2HDR";
    private const string TaskPath = @"\Go2HDR";
    private const string AppName = "Go2HDR";
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ChangeTimeout = TimeSpan.FromSeconds(20);

    private static string ExePath => Environment.ProcessPath
        ?? throw new InvalidOperationException("The current executable path is unavailable.");

    public async Task<bool?> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        if (IsRegistryInstalled()) return true;
        return await IsTaskInstalledAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> InstallAsync(CancellationToken cancellationToken = default)
    {
        bool created = await CreateTaskAsync(cancellationToken).ConfigureAwait(false);
        if (!created) return false;

        bool? verified = await IsTaskInstalledAsync(cancellationToken).ConfigureAwait(false);
        if (verified == false)
        {
            AppLog.Write("The autostart task creation command succeeded, but the task was not found afterwards.");
            return false;
        }

        RemoveRegistry();
        return true;
    }

    public async Task<bool> RemoveAsync(CancellationToken cancellationToken = default)
    {
        bool removed = await DeleteTaskAsync(cancellationToken).ConfigureAwait(false);
        if (!removed) return false;

        RemoveRegistry();
        bool? stillInstalled = await IsTaskInstalledAsync(cancellationToken).ConfigureAwait(false);
        return stillInstalled != true;
    }

    // Migrate a legacy Run entry and update a task whose executable moved after an
    // installation change. This must never delay application startup.
    public async Task SyncPathAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            bool legacyInstalled = IsRegistryInstalled();
            bool? taskInstalled = await IsTaskInstalledAsync(cancellationToken).ConfigureAwait(false);

            if (taskInstalled == true)
            {
                string? taskExecutable = await GetTaskExecutableAsync(cancellationToken).ConfigureAwait(false);
                if (taskExecutable is not null && !PathsEqual(taskExecutable, ExePath))
                {
                    await InstallAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (legacyInstalled)
                {
                    RemoveRegistry();
                }
            }
            else if (legacyInstalled)
            {
                await InstallAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppLog.Write("Synchronizing the autostart task failed.", ex);
        }
    }

    private static async Task<bool?> IsTaskInstalledAsync(CancellationToken cancellationToken)
    {
        ProcessResult? result = await RunProcessAsync(
            CreateSchtasksStartInfo("/Query", "/TN", TaskPath),
            QueryTimeout,
            "querying the autostart task",
            cancellationToken).ConfigureAwait(false);
        return result?.ExitCode == 0 ? true : result is null ? null : false;
    }

    private static async Task<string?> GetTaskExecutableAsync(CancellationToken cancellationToken)
    {
        ProcessResult? result = await RunProcessAsync(
            CreateSchtasksStartInfo("/Query", "/TN", TaskPath, "/XML"),
            QueryTimeout,
            "reading the autostart task",
            cancellationToken).ConfigureAwait(false);

        return result is { ExitCode: 0 } &&
            TryGetTaskExecutable(result.StandardOutput, out string? executable)
                ? executable
                : null;
    }

    internal static bool TryGetTaskExecutable(string xml, out string? executable)
    {
        executable = null;
        if (string.IsNullOrWhiteSpace(xml)) return false;
        try
        {
            var document = XDocument.Parse(xml, LoadOptions.None);
            executable = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Command")?.Value.Trim();
            return !string.IsNullOrWhiteSpace(executable);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task<bool> CreateTaskAsync(CancellationToken cancellationToken)
    {
        string exe = ExePath.Replace("'", "''", StringComparison.Ordinal);
        string workingDirectory = Path.GetDirectoryName(ExePath)!
            .Replace("'", "''", StringComparison.Ordinal);
        string script = $$"""
            try {
                $ErrorActionPreference = 'Stop'
                $action   = New-ScheduledTaskAction -Execute '{{exe}}' -WorkingDirectory '{{workingDirectory}}'
                $user     = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
                $trigger  = New-ScheduledTaskTrigger -AtLogOn -User $user
                $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Seconds 0)
                Register-ScheduledTask -TaskName 'Go2HDR' -TaskPath '\' -Action $action -Trigger $trigger -Settings $settings -RunLevel Limited -Force | Out-Null
                exit 0
            } catch {
                [Console]::Error.WriteLine($_.Exception.Message)
                exit 1
            }
            """;
        return await RunPowerShellAsync(script, "creating the autostart task", cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<bool> DeleteTaskAsync(CancellationToken cancellationToken)
    {
        const string script = """
            try {
                $ErrorActionPreference = 'Stop'
                $task = Get-ScheduledTask -TaskName 'Go2HDR' -TaskPath '\' -ErrorAction SilentlyContinue
                if ($null -ne $task) {
                    Unregister-ScheduledTask -TaskName 'Go2HDR' -TaskPath '\' -Confirm:$false
                }
                exit 0
            } catch {
                [Console]::Error.WriteLine($_.Exception.Message)
                exit 1
            }
            """;
        return await RunPowerShellAsync(script, "removing the autostart task", cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<bool> RunPowerShellAsync(
        string script, string operation, CancellationToken cancellationToken)
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        string powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var psi = new ProcessStartInfo(powershell)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-WindowStyle");
        psi.ArgumentList.Add("Hidden");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(encoded);

        ProcessResult? result = await RunProcessAsync(
            psi, ChangeTimeout, operation, cancellationToken).ConfigureAwait(false);
        if (result is null) return false;
        if (result.ExitCode != 0 && !string.IsNullOrWhiteSpace(result.StandardError))
            AppLog.Write($"PowerShell failed while {operation}: {result.StandardError.Trim()}");
        return result.ExitCode == 0;
    }

    private static ProcessStartInfo CreateSchtasksStartInfo(params string[] arguments)
    {
        var psi = new ProcessStartInfo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"))
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments)
            psi.ArgumentList.Add(argument);
        return psi;
    }

    private static async Task<ProcessResult?> RunProcessAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) return null;

            // Keep draining both redirected pipes until the process exits (or is killed).
            // Cancelling a read before killing the child can leave a full pipe behind.
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); }
                catch { }
                try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
                catch { }
                try { await Task.WhenAll(standardOutput, standardError).ConfigureAwait(false); }
                catch { }
                if (!cancellationToken.IsCancellationRequested)
                    AppLog.Write($"Timed out while {operation}.");
                return null;
            }

            return new ProcessResult(
                process.ExitCode,
                await standardOutput.ConfigureAwait(false),
                await standardError.ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            AppLog.Write($"Failed while {operation}.", ex);
            return null;
        }
    }

    private static bool IsRegistryInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) is not null;
        }
        catch (Exception ex)
        {
            AppLog.Write("Querying the legacy autostart entry failed.", ex);
            return false;
        }
    }

    private static void RemoveRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            AppLog.Write("Removing the legacy autostart entry failed.", ex);
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return Path.GetFullPath(left)
                .Equals(Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
