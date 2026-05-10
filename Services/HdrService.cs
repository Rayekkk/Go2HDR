namespace Go2HDR.Services;

public class HdrService : IDisposable
{
    private readonly DisplayConfigService _display;
    private readonly BrightnessService _brightness;
    private readonly SettingsService _settings;
    private volatile bool _hdrActive;

    public event Action<bool>? HdrStateChanged;
    public event Action<byte>? BrightnessChanged;

    public bool IsHdrActive => _hdrActive;

    public HdrService(DisplayConfigService display, BrightnessService brightness, SettingsService settings)
    {
        _display = display;
        _brightness = brightness;
        _settings = settings;

        _brightness.BrightnessChanged += OnBrightnessChanged;
    }

    public void Start()
    {
        Poll();
    }

    public void Stop()
    {
        _brightness.StopWatching();
    }

    public void Poll()
    {
        bool hdrNow = _display.IsBuiltInHdrActive();
        if (hdrNow == _hdrActive) return;

        _hdrActive = hdrNow;
        HdrStateChanged?.Invoke(_hdrActive);

        if (_hdrActive)
        {
            _brightness.StartWatching();
            RefreshSdr();
        }
        else
        {
            _brightness.StopWatching();
        }
    }

    // Reads the current brightness, notifies listeners, and applies the SDR level.
    // Call this whenever the state changes and an immediate sync is needed.
    public void RefreshSdr()
    {
        if (!_hdrActive) return;
        byte current = _brightness.GetCurrentBrightness();
        BrightnessChanged?.Invoke(current);
        ApplySdr(current);
    }

    private void OnBrightnessChanged(byte b)
    {
        BrightnessChanged?.Invoke(b);
        if (_hdrActive) ApplySdr(b);
    }

    public void ApplySdr(byte brightness)
    {
        if (!_settings.Current.IsEnabled) return;
        int sdr = _settings.GetSdrValue(brightness);
        _display.SetSdrWhiteLevel(sdr);
    }

    public void Dispose()
    {
        Stop();
        _brightness.BrightnessChanged -= OnBrightnessChanged;
    }
}

