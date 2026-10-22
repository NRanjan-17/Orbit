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
    private readonly BluetoothRadioMonitor _radio = new();
    // Restarting scanning after Windows stopped it: first try after 2 s, backing off to every 15 s.
    private readonly RetryBackoff _scanRetry = new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(15));
    private readonly DispatcherQueueTimer _pruneTimer;
    private DeviceTracker _tracker;
    private AutoPauseController _autoPause;
    private readonly AutoOutputController _autoOutput = new(new Platform.Audio.WindowsAudioOutputs());
    private LowBatteryMonitor _lowBattery;

    public OrbitService(DispatcherQueue ui, SettingsStore store)
    {
        _ui = ui;
        Store = store;
        Settings = store.Load();
        Display.EstimateBattery = Settings.EstimateBattery;
        _tracker = new DeviceTracker(null, Settings.RssiThreshold);
        _autoPause = CreateAutoPause();
        _autoOutput.Enabled = Settings.AutoSwitchOutput;
        _lowBattery = new LowBatteryMonitor(Settings.LowBatteryPercent);

        _scanner.AdvertisementReceived += (_, a) => _ui.TryEnqueue(() => OnAdvertisement(a));
        _scanner.Stopped += (_, error) => _ui.TryEnqueue(() =>
        {
            ScannerError = ScannerStoppedMessage();
            Log.Write($"scanner stopped: {error}");
            _scanRetry.Failed(DateTimeOffset.UtcNow);
            Changed?.Invoke(this, EventArgs.Empty);
        });

        // Bluetooth switched back on: resume scanning right away instead of staying stuck on the error.
        _radio.StateChanged += (_, on) => _ui.TryEnqueue(() =>
        {
            Log.Write($"bluetooth radio {(on ? "on" : "off")}");
            if (on && ScannerError is not null)
            {
                RestartScanner();
            }
        });

        _usb.Changed += (_, _) => _ui.TryEnqueue(() =>
        {
            Log.Write($"usb audio: {(_usb.IsConnected ? "connected (" + _usb.DeviceName + ")" : "disconnected")}");
            Changed?.Invoke(this, EventArgs.Empty);
        });

        _pruneTimer = ui.CreateTimer();
        _pruneTimer.Interval = TimeSpan.FromSeconds(5);
        _pruneTimer.Tick += (_, _) =>
        {
            // Fallback when no radio event arrives (adapter re-plugged, radio API unavailable).
            if (ScannerError is not null && _scanRetry.Due(DateTimeOffset.UtcNow))
            {
                RestartScanner();
            }

            if (_tracker.Prune(DateTimeOffset.UtcNow))
            {
                // Auto-output keeps its pending switch-back: the headphones often sleep while off-head.
                _autoPause.Reset();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public event EventHandler? Changed;

    /// <summary>Starts scanning again after it stopped; clears the error on success, backs off on failure.</summary>
    private void RestartScanner()
    {
        try
        {
            _scanner.Restart();
            ScannerError = null;
            _scanRetry.Succeeded();
            Log.Write("scanner restarted");
        }
        catch (Exception ex)
        {
            ScannerError = ScannerStoppedMessage();
            Log.Write($"scanner restart failed: {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
            _scanRetry.Failed(DateTimeOffset.UtcNow);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// What to tell the user when scanning stops. Windows' own codes ("Success", empty messages) mean nothing
    /// to people, so they go to the log only.
    /// </summary>
    private string ScannerStoppedMessage() => _radio.IsOn switch
    {
        false => "Bluetooth is off. Turn it on in Quick Settings and Orbit picks your AirPods up again automatically.",
        _ => "Orbit lost its Bluetooth connection and is reconnecting. If this doesn't clear in a minute, turn Bluetooth off and on.",
    };

    private readonly Platform.Audio.UsbAudioMonitor _usb = new();

    private int? _localAddressSuffix;

    /// <summary>
    /// Which device the headphones are connected to, from the latest fresh decrypted broadcast (needs the
    /// exact-battery key). Unknown while asleep or without a key.
    /// </summary>
    public Core.Switching.ConnectedTo ConnectedTo => Current is { } c
        ? Core.Switching.ConnectedHost.Classify(c.Status.ConnectedHostSuffix, _localAddressSuffix)
        : Core.Switching.ConnectedTo.Unknown;

    /// <summary>True while Apple headphones are connected to this PC by USB-C cable (USB Audio).</summary>
    public bool UsbConnected => _usb.IsConnected;

    public string? UsbDeviceName => _usb.DeviceName;

    /// <summary>Raised with (device name, battery percent) when the battery crosses the warning threshold.</summary>
    public event EventHandler<(string Name, int Percent)>? LowBattery;

    public event EventHandler<AutoPauseAction>? AutoPaused;

    /// <summary>Raised when auto-output moved the Windows default audio output.</summary>
    public event EventHandler<AutoOutputAction>? OutputSwitched;

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
        try
        {
            _localAddressSuffix = await Platform.Bluetooth.LocalAdapter.GetAddressSuffixAsync();
        }
        catch (Exception ex)
        {
            Log.Error("local adapter", ex);
        }

        await RefreshBindingAsync();
        await _radio.StartAsync();
        try
        {
            _scanner.Start();
            ScannerError = null;
        }
        catch (Exception ex)
        {
            ScannerError = $"Could not start Bluetooth scanning: {ex.Message}";
            _scanRetry.Failed(DateTimeOffset.UtcNow); // e.g. Bluetooth off at launch: retry until it's on
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
        Display.EstimateBattery = Settings.EstimateBattery;
        Store.Save(Settings);
        _tracker.RssiThreshold = Settings.RssiThreshold;
        if (Settings.AutoPauseResponse != _autoPauseResponse)
        {
            _autoPause = CreateAutoPause();
        }

        _autoPause.PauseEnabled = Settings.AutoPause;
        _autoPause.ResumeEnabled = Settings.AutoResume;
        _autoOutput.Enabled = Settings.AutoSwitchOutput;
        _lowBattery.ThresholdPercent = Settings.LowBatteryPercent;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private readonly Platform.KeyStore _keyStore = new(Path.Combine(AppPaths.DataDirectory, "device-key.bin"));

    /// <summary>(different broadcasts checked, consistent decodes so far) while a key is being checked.</summary>
    public (int Seen, int Consistent) KeyProgress => _tracker.Exact is { } e ? (e.DistinctTailsSeen, e.BestRun) : (0, 0);

    /// <summary>True when a key is stored on this PC (whatever its check state).</summary>
    public bool HasStoredKey => _keyStore.HasKey;

    /// <summary>State of the user's encryption key for exact battery (None / Checking / Confirmed / Rejected).</summary>
    public Core.Crypto.KeyState KeyState => _tracker.Exact?.State ?? Core.Crypto.KeyState.None;

    /// <summary>Parses and stores the key (bare hex or the whole keychain plist). Returns false if not a 16-byte key.</summary>
    public bool SetKey(string text)
    {
        if (!Core.Crypto.ProximityKey.TryParse(text, out var key))
        {
            return false;
        }

        _keyStore.Save(key);
        _tracker.Exact = new Core.Crypto.ExactBatteryResolver(key);
        Log.Write("exact battery key saved (DPAPI)");
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private Core.Crypto.KeyState _loggedKeyState;
    private int _loggedChecks;

    /// <summary>Writes key-check progress to orbit.log: each state change, and the first few check frames.</summary>
    private void LogKeyProgress()
    {
        if (_tracker.Exact is not { } exact)
        {
            return;
        }

        // One line per NEW broadcast examined (repeats of the same broadcast are not logged).
        if (exact.State == Core.Crypto.KeyState.Checking && exact.DistinctTailsSeen != _loggedChecks)
        {
            _loggedChecks = exact.DistinctTailsSeen;
            Log.Write($"exact battery check: {exact.LastCheckDiagnostics}");
        }

        if (exact.State != _loggedKeyState)
        {
            _loggedKeyState = exact.State;
            Log.Write($"exact battery key state: {exact.State}{(exact.UsedReversedKey ? " (reversed byte order)" : "")}");
            RememberKeyConfirmation(exact);
        }
    }

    /// <summary>
    /// Remembers that the stored key was confirmed (and in which byte order) so the next launch can show the
    /// exact battery immediately instead of re-checking for minutes. Stored as a short SHA-256 fingerprint of
    /// the key, never the key itself.
    /// </summary>
    private void RememberKeyConfirmation(Core.Crypto.ExactBatteryResolver exact)
    {
        var key = _keyStore.Load();
        if (exact.State == Core.Crypto.KeyState.Confirmed && key is not null)
        {
            Settings.ExactKeyFingerprint = KeyFingerprint(key);
            Settings.ExactKeyReversed = exact.UsedReversedKey;
        }
        else if (exact.State is Core.Crypto.KeyState.Rejected or Core.Crypto.KeyState.Checking)
        {
            Settings.ExactKeyFingerprint = null;
            Settings.ExactKeyReversed = null;
        }

        Store.Save(Settings);
    }

    private static string KeyFingerprint(byte[] key) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(key).AsSpan(0, 8));

    /// <summary>Resolver for the stored key, starting Confirmed when this exact key was confirmed before.</summary>
    private Core.Crypto.ExactBatteryResolver? CreateResolver()
    {
        if (_keyStore.Load() is not { } key)
        {
            return null;
        }

        var fingerprint = KeyFingerprint(key);
        var remembered = Settings.ExactKeyFingerprint == fingerprint ? Settings.ExactKeyReversed : null;
        return new Core.Crypto.ExactBatteryResolver(key, remembered)
        {
            UserVerified = Settings.ExactBatteryVerifiedFingerprint == fingerprint,
        };
    }

    /// <summary>
    /// For the Debug Capture page: decrypts a Proximity Pairing payload of the bound device with the confirmed
    /// key and returns the plaintext as hex (null if no confirmed key or not the bound device). Used to look for
    /// state such as noise mode inside the encrypted part. Plaintext only; the key is never exposed.
    /// </summary>
    public Func<byte[], string?>? CreateCaptureDecryptor()
    {
        if (_keyStore.Load() is not { } key || _tracker.Exact is not { State: Core.Crypto.KeyState.Confirmed } exact || BoundProfile is null)
        {
            return null;
        }

        var effective = exact.UsedReversedKey ? key.Reverse().ToArray() : key;
        var model = BoundProfile.ModelId;
        return payload =>
            Core.Parsing.ProximityPairingFrame.TryParse(payload, out var f) && f is not null && f.ModelId == model
                ? Convert.ToHexString(Core.Crypto.ProximityKey.DecryptBlock(f.EncryptedTail, effective))
                : null;
    }

    /// <summary>True when the owner confirmed the exact battery matches their Mac for the stored key.</summary>
    public bool ExactBatteryVerified => _tracker.Exact?.UserVerified == true;

    /// <summary>Owner confirms the decoded exact battery matches the battery shown on their Mac.</summary>
    public void MarkExactBatteryVerified()
    {
        if (_keyStore.Load() is not { } key || _tracker.Exact is not { } exact)
        {
            return;
        }

        exact.UserVerified = true;
        Settings.ExactBatteryVerifiedFingerprint = KeyFingerprint(key);
        Store.Save(Settings);
        Log.Write($"exact battery verified by owner against Mac (Orbit showed {LastKnown?.Status.ExactBattery.Value}%)");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveKey()
    {
        _keyStore.Delete();
        _tracker.Exact = null;
        Settings.ExactKeyFingerprint = null;
        Settings.ExactKeyReversed = null;
        Settings.ExactBatteryVerifiedFingerprint = null;
        Store.Save(Settings);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyBinding(PairedDevice? device)
    {
        BoundDevice = device;
        BoundProfile = device is null ? null : BoundDeviceProfile.Create(device.Name, device.VendorId, device.ProductId, device.ClassOfDevice);
        _tracker = new DeviceTracker(BoundProfile, Settings.RssiThreshold)
        {
            Exact = CreateResolver(),
        };
        _loggedKeyState = _tracker.Exact?.State ?? Core.Crypto.KeyState.None;
        _autoPause = CreateAutoPause();
        _autoOutput.Reset();
        _autoOutput.HeadphonesContainer = device?.ContainerId;
        LastKnown = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string? _autoPauseResponse;

    private AutoPauseController CreateAutoPause() => (_autoPauseResponse = Settings.AutoPauseResponse) == AutoPauseResponses.Steady
        ? new(new SystemMediaController(), requiredReadings: 2, minimumDuration: TimeSpan.FromSeconds(1.5))
        {
            PauseEnabled = Settings.AutoPause,
            ResumeEnabled = Settings.AutoResume,
        }
        : new(new SystemMediaController(), requiredReadings: 1, minimumDuration: TimeSpan.Zero)
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
        LogKeyProgress();
        Changed?.Invoke(this, EventArgs.Empty);

        if (Settings.LowBatteryToast && _lowBattery.Check(status.LowestBattery, status.Charging) is { } pct)
        {
            LowBattery?.Invoke(this, (DisplayName, pct));
        }

        var wornBefore = _autoPause.Worn;
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

        // Debounced, verified on-head transitions only (not the first reading after start-up).
        if (wornBefore is not null && _autoPause.Worn is { } worn && worn != wornBefore)
        {
            SwitchOutput(worn);
            WornChanged?.Invoke(this, worn);
        }
        else if (wornBefore is null && _autoPause.Worn == true && _autoOutput.HoldingSwitch)
        {
            // Woke up already on-head after sleeping while off-head (tracking was reset meanwhile): still put the
            // sound back on the headphones.
            SwitchOutput(true);
        }
    }

    private void SwitchOutput(bool worn)
    {
        try
        {
            var action = _autoOutput.OnWornChanged(worn);
            if (action != AutoOutputAction.None)
            {
                Log.Write($"auto-output: {action}");
                OutputSwitched?.Invoke(this, action);
            }
        }
        catch (Exception ex)
        {
            // Core Audio can fail while an endpoint is being added or removed; skip this transition.
            Log.Error("auto-output", ex);
        }
    }

    /// <summary>Raised when the debounced, verified on-head state changes (true = put on).</summary>
    public event EventHandler<bool>? WornChanged;

    /// <summary>
    /// Device name for display. Windows shows Apple devices registered with Find My as "Name - Find My";
    /// that suffix (and the double space before it) is dropped here.
    /// </summary>
    public string DisplayName => CleanName(BoundDevice?.Name) ?? LastKnown?.Status.ModelName ?? "No device";

    private static string? CleanName(string? name)
    {
        if (name is null)
        {
            return null;
        }

        const string suffix = "- Find My";
        var trimmed = name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? name[..^suffix.Length] : name;
        return string.Join(' ', trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public void Dispose()
    {
        _pruneTimer.Stop();
        _scanner.Dispose();
        _radio.Dispose();
        _usb.Dispose();
    }
}
