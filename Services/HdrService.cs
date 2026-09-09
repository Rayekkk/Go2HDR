namespace Go2HDR.Services;

public sealed class HdrService : IDisposable
{
    private readonly DisplayConfigService _display;
    private readonly BrightnessService _brightness;
    private readonly SettingsService _settings;
    private volatile bool _hdrActive;
    private volatile bool _isStateAvailable;
    private bool _stateUnavailableLogged;
    private bool _disposed;

    public event Action<bool>? HdrStateChanged;
    public event Action<bool>? HdrAvailabilityChanged;
    public event Action<byte>? BrightnessChanged;

    public bool IsHdrActive => _hdrActive;
    public bool IsStateAvailable => _isStateAvailable;

    public HdrService(DisplayConfigService display, BrightnessService brightness, SettingsService settings)
    {
        _display = display;
        _brightness = brightness;
        _settings = settings;

        _brightness.BrightnessChanged += OnBrightnessChanged;
        _settings.Changed += OnSettingsChanged;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Poll(forceRefresh: true);
    }

    public void Stop()
    {
        _brightness.StopWatching();
    }

    public void Poll(bool forceRefresh = false)
    {
        if (_disposed) return;

        BuiltInHdrState state = _display.GetBuiltInHdrState();
        if (state == BuiltInHdrState.Unavailable)
        {
            if (!_stateUnavailableLogged)
                AppLog.Write("HDR state is temporarily unavailable; preserving the previous state.");
            _stateUnavailableLogged = true;
            if (_isStateAvailable)
            {
                _isStateAvailable = false;
                HdrAvailabilityChanged?.Invoke(false);
            }
            return;
        }
        _stateUnavailableLogged = false;
        if (!_isStateAvailable)
        {
            _isStateAvailable = true;
            HdrAvailabilityChanged?.Invoke(true);
        }

        bool hdrNow = state == BuiltInHdrState.Active;
        if (hdrNow == _hdrActive)
        {
            if (forceRefresh && hdrNow)
            {
                // Display topology and resume events can invalidate an otherwise live
                // WMI subscription. Recreate it even when the HDR state did not change.
                _brightness.RestartWatching();
                RefreshSdr();
            }
            return;
        }

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
        if (!_brightness.TryGetCurrentBrightness(out byte current))
        {
            AppLog.Write("Skipped SDR refresh because the current display brightness is unavailable.");
            return;
        }
        BrightnessChanged?.Invoke(current);
        ApplySdr(current);
    }

    private void OnBrightnessChanged(byte b)
    {
        BrightnessChanged?.Invoke(b);
        if (_hdrActive) ApplySdr(b);
    }

    private void OnSettingsChanged()
    {
        if (_hdrActive) RefreshSdr();
    }

    public void ApplySdr(byte brightness)
    {
        if (!_settings.Current.IsEnabled) return;
        int sdr = _settings.GetSdrValue(brightness);
        _display.SetSdrWhiteLevel(sdr);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _brightness.BrightnessChanged -= OnBrightnessChanged;
        _settings.Changed -= OnSettingsChanged;
    }
}
