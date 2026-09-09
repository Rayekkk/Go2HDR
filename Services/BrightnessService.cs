using System.Management;

namespace Go2HDR.Services;

public sealed class BrightnessService : IDisposable
{
    private readonly object _watcherLock = new();
    private ManagementEventWatcher? _watcher;
    private int _lastBrightness = -1;

    public event Action<byte>? BrightnessChanged;

    public bool TryGetCurrentBrightness(out byte brightness)
    {
        brightness = 0;
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi",
                "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active = TRUE");
            using var results = searcher.Get();
            foreach (ManagementBaseObject obj in results)
            {
                using (obj)
                {
                    brightness = (byte)obj["CurrentBrightness"];
                    Interlocked.Exchange(ref _lastBrightness, brightness);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Reading current display brightness through WMI failed.", ex);
        }
        return false;
    }

    public void StartWatching()
    {
        lock (_watcherLock)
        {
            if (_watcher != null) return;
            ManagementEventWatcher? watcher = null;
            try
            {
                // Some display drivers (including the Legion Go 2 panel driver) expose
                // WmiMonitorBrightnessEvent but do not actually emit it. Watching the
                // WmiMonitorBrightness instance itself is the proven, driver-independent
                // path and keeps working while the window is hidden in the tray.
                watcher = new ManagementEventWatcher(
                    new ManagementScope(@"root\wmi"),
                    new WqlEventQuery(
                        "SELECT * FROM __InstanceModificationEvent WITHIN 1 " +
                        "WHERE TargetInstance ISA 'WmiMonitorBrightness'"));
                watcher.EventArrived += OnEventArrived;
                watcher.Start();
                _watcher = watcher;
            }
            catch (Exception ex)
            {
                AppLog.Write("Starting the WMI brightness watcher failed.", ex);
                if (watcher is not null)
                {
                    watcher.EventArrived -= OnEventArrived;
                    watcher.Dispose();
                }
                _watcher = null;
            }
        }
    }

    public void StopWatching()
    {
        lock (_watcherLock)
        {
            if (_watcher == null) return;
            var watcher = _watcher;
            _watcher = null;
            watcher.EventArrived -= OnEventArrived;
            try { watcher.Stop(); }
            catch (Exception ex)
            {
                AppLog.Write("Stopping the WMI brightness watcher failed.", ex);
            }
            try { watcher.Dispose(); }
            catch (Exception ex)
            {
                AppLog.Write("Disposing the WMI brightness watcher failed.", ex);
            }
        }
    }

    public void RestartWatching()
    {
        StopWatching();
        StartWatching();
    }

    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            if (e.NewEvent["TargetInstance"] is not ManagementBaseObject target) return;
            using (target)
            {
                byte b = Convert.ToByte(
                    target["CurrentBrightness"], System.Globalization.CultureInfo.InvariantCulture);
                if (Interlocked.Exchange(ref _lastBrightness, b) == b) return;
                BrightnessChanged?.Invoke(b);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Processing a WMI brightness event failed.", ex);
        }
    }

    public void Dispose() => StopWatching();
}
