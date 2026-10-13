using Microsoft.UI.Dispatching;
using Orbit.Core.Media;
using Orbit.Core.Parsing;
using Orbit.Core.Settings;
using Orbit.Core.Tracking;
using Orbit.Platform.Bluetooth;
using Orbit.Platform.Media;

namespace Orbit.Services;

/// <summary>
/// App-wide background service: BLE scanning → device tracking → auto-pause and low-battery alerts.
/// All state changes are marshalled to the UI thread, and <see cref="Changed"/> is raised there.
/// </summary>
public sealed class OrbitService : IDisposable
{
    private readonly DispatcherQueue _ui;
    private readonly AppleAdvertisementScanner _scanner = new();
    private readonly DispatcherQueueTimer _pruneTimer;
    private DeviceTracker _tracker;
    private AutoPauseController _autoPause;
    private LowBatteryMonitor _lowBattery;

    public OrbitService(DispatcherQueue ui, SettingsStore store)
    {
        _ui = ui;
        Store = store;
        Settings = store.Load();
        _tracker = new DeviceTracker(null, Settings.RssiThreshold);
        _autoPause = CreateAutoPause();
        _lowBattery = new LowBatteryMonitor(Settings.LowBatteryPercent);

        _scanner.AdvertisementReceived += (_, a) => _ui.TryEnqueue(() => OnAdvertisement(a));
        _scanner.Stopped += (_, error) => _ui.TryEnqueue(() =>
        {
            ScannerError = $"Bluetooth scanning stopped ({error}). Is Bluetooth turned on?";
            Changed?.Invoke(this, EventArgs.Empty);
        });

        _pruneTimer = ui.CreateTimer();
        _pruneTimer.Interval = TimeSpan.FromSeconds(5);
        _pruneTimer.Tick += (_, _) =>
        {
            if (_tracker.Prune(DateTimeOffset.UtcNow))
            {
                _autoPause.Reset();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public event EventHandler? Changed;

    /// <summary>Raised with (device name, battery percent) when the battery crosses the warning threshold.</summary>
    public event EventHandler<(string Name, int Percent)>? LowBattery;

    public event EventHandler<AutoPauseAction>? AutoPaused;

    public SettingsStore Store { get; }

    public OrbitSettings Settings { get; }

    public PairedDevice? BoundDevice { get; private set; }

    public ModelProfile? BoundProfile { get; private set; }

    public IReadOnlyList<PairedDevice> PairedAppleDevices { get; private set; } = [];

    /// <summary>The device currently being heard, or null when it is asleep / out of range.</summary>
    public TrackedDevice? Current => _tracker.Current;

    /// <summary>The last device state seen, kept after the device goes quiet so the UI can show "last seen".</summary>
    public TrackedDevice? LastKnown { get; private set; }

    public bool? StableWorn => _autoPause.Worn;

    public string? ScannerError { get; private set; }

    public async Task StartAsync()
    {
        await RefreshBindingAsync();
        try
        {
            _scanner.Start();
            ScannerError = null;
        }
        catch (Exception ex)
        {
            ScannerError = $"Could not start Bluetooth scanning: {ex.Message}";
        }

        _pruneTimer.Start();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Resolves the bound device from settings. If none is set and exactly one Apple Device-ID device is paired,
    /// binds it automatically. The binding (a Windows device ID) is stored only in local settings.
    /// </summary>
    public async Task RefreshBindingAsync()
    {
        try
        {
            PairedAppleDevices = (await PairedDeviceCatalog.GetPairedAsync()).Where(d => d.IsApple).ToList();
        }
        catch (Exception)
        {
            PairedAppleDevices = [];
        }

        var bound = PairedAppleDevices.FirstOrDefault(d => d.DeviceId == Settings.BoundDeviceId);
        if (bound is null && Settings.BoundDeviceId is null && PairedAppleDevices.Count == 1)
        {
            bound = PairedAppleDevices[0];
            Settings.BoundDeviceId = bound.DeviceId;
            Store.Save(Settings);
        }

        ApplyBinding(bound);
    }

    public void Bind(PairedDevice? device)
    {
        Settings.BoundDeviceId = device?.DeviceId;
        Store.Save(Settings);
        ApplyBinding(device);
    }

    /// <summary>Re-applies settings that affect tracking/auto-pause and saves them.</summary>
    public void SaveSettings()
    {
        Store.Save(Settings);
        _tracker.RssiThreshold = Settings.RssiThreshold;
        _autoPause.PauseEnabled = Settings.AutoPause;
        _autoPause.ResumeEnabled = Settings.AutoResume;
        _lowBattery.ThresholdPercent = Settings.LowBatteryPercent;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyBinding(PairedDevice? device)
    {
        BoundDevice = device;
        BoundProfile = device is null ? null : BoundDeviceProfile.Create(device.Name, device.VendorId, device.ProductId, device.ClassOfDevice);
        _tracker = new DeviceTracker(BoundProfile, Settings.RssiThreshold);
        _autoPause = CreateAutoPause();
        LastKnown = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private AutoPauseController CreateAutoPause() => new(new SystemMediaController())
    {
        PauseEnabled = Settings.AutoPause,
        ResumeEnabled = Settings.AutoResume,
    };

    private async void OnAdvertisement(RawAppleAdvertisement a)
    {
        var status = _tracker.Add(a.Payload, a.Rssi, a.TimestampUtc);
        if (status is null)
        {
            return;
        }

        LastKnown = _tracker.Current;
        Changed?.Invoke(this, EventArgs.Empty);

        if (Settings.LowBatteryToast && _lowBattery.Check(status.LowestBattery, status.Charging) is { } pct)
        {
            LowBattery?.Invoke(this, (DisplayName, pct));
        }

        try
        {
            var action = await _autoPause.OnReadingAsync(status.AnyWorn, a.TimestampUtc);
            if (action != AutoPauseAction.None)
            {
                AutoPaused?.Invoke(this, action);
            }
        }
        catch (Exception)
        {
            // Media session APIs can throw if the owning app exits mid-call; auto-pause just skips this reading.
        }
    }

    public string DisplayName => BoundDevice?.Name ?? LastKnown?.Status.ModelName ?? "No device";

    public void Dispose()
    {
        _pruneTimer.Stop();
        _scanner.Dispose();
    }
}
