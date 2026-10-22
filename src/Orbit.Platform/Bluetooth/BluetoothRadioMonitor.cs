using Windows.Devices.Radios;

namespace Orbit.Platform.Bluetooth;

/// <summary>
/// Reports when the PC's Bluetooth radio is switched on or off (Settings, Action Center, airplane mode), so
/// scanning can restart as soon as Bluetooth is back. Best effort: if the radio API is unavailable, Orbit's
/// periodic retry still recovers.
/// </summary>
public sealed class BluetoothRadioMonitor : IDisposable
{
    private Radio? _radio;

    /// <summary>Raised with true when the radio turns on, false when it turns off. Any thread.</summary>
    public event EventHandler<bool>? StateChanged;

    public bool? IsOn => _radio is { } r ? r.State == RadioState.On : null;

    public async Task StartAsync()
    {
        try
        {
            _radio = (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            if (_radio is not null)
            {
                _radio.StateChanged += OnStateChanged;
            }
        }
        catch (Exception)
        {
            // No radio access (rare on desktop); the retry timer covers this.
            _radio = null;
        }
    }

    private void OnStateChanged(Radio sender, object args) => StateChanged?.Invoke(this, sender.State == RadioState.On);

    public void Dispose()
    {
        if (_radio is not null)
        {
            _radio.StateChanged -= OnStateChanged;
        }
    }
}
