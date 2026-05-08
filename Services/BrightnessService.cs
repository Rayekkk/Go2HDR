using System.Management;

namespace Go2HDR.Services;

public class BrightnessService : IDisposable
{
    private ManagementEventWatcher? _watcher;

    public event Action<byte>? BrightnessChanged;

    public byte GetCurrentBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightness");
            using var results  = searcher.Get();
            foreach (ManagementBaseObject obj in results)
            {
                using (obj) return (byte)obj["CurrentBrightness"];
            }
        }
        catch { }
        return 50;
    }

    public void StartWatching()
    {
        if (_watcher != null) return;
        try
        {
            _watcher = new ManagementEventWatcher(
                new ManagementScope(@"root\wmi"),
                new WqlEventQuery("SELECT * FROM __InstanceModificationEvent WITHIN 1 WHERE TargetInstance ISA 'WmiMonitorBrightness'"));
            _watcher.EventArrived += OnEventArrived;
            _watcher.Start();
        }
        catch { _watcher = null; }
    }

    public void StopWatching()
    {
        if (_watcher == null) return;
        _watcher.Stop();
        _watcher.Dispose();
        _watcher = null;
    }

    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            byte b = (byte)((ManagementBaseObject)e.NewEvent["TargetInstance"])["CurrentBrightness"];
            BrightnessChanged?.Invoke(b);
        }
        catch { }
    }

    public void Dispose() => StopWatching();
}
