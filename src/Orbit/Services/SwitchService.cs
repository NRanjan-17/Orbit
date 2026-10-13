using Microsoft.UI.Dispatching;
using Orbit.Core.Settings;
using Orbit.Core.Switching;
using Orbit.Platform;
using Orbit.Platform.Bluetooth;
using Orbit.Platform.Media;

namespace Orbit.Services;

/// <summary>
/// Multi-device switching for the bound headphones: "Switch to this PC", "Release to other device", the global
/// hotkey, and optional auto-connect when Windows playback starts while the headphones are nearby but not
/// connected. Apple's iCloud auto-switching is not available on Windows (docs/multi-device.md).
/// </summary>
public sealed class SwitchService : IDisposable
{
    private readonly DispatcherQueue _ui;
    private readonly OrbitService _orbit;
    private readonly SystemMediaController _media = new();
    private readonly AutoConnectPolicy _autoConnect = new();
    private readonly DispatcherQueueTimer _playbackTimer;
    private ConnectionMonitor? _monitor;
    private SwitchCoordinator? _coordinator;
    private IAudioConnector? _connector;
    private GlobalHotkey? _hotkey;
    private string? _boundId;
    private string? _method;

    public SwitchService(DispatcherQueue ui, OrbitService orbit)
    {
        _ui = ui;
        _orbit = orbit;
        _orbit.Changed += async (_, _) => await SyncAsync();
        _playbackTimer = ui.CreateTimer();
        _playbackTimer.Interval = TimeSpan.FromSeconds(2);
        _playbackTimer.Tick += async (_, _) => await CheckAutoConnectAsync();
        _playbackTimer.Start();
    }

    /// <summary>Raised on the UI thread whenever connection state or the last result changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised after every switch attempt (button, hotkey or auto-connect).</summary>
    public event EventHandler<SwitchResult>? Completed;

    public bool IsAvailable => _coordinator is not null;

    public bool IsConnected => _monitor?.IsConnected == true;

    public bool IsBusy { get; private set; }

    public SwitchResult? LastResult { get; private set; }

    /// <summary>Connector diagnostics from the last attempt (no addresses).</summary>
    public string LastDiagnostics => _connector switch
    {
        KsAudioConnector k => k.LastDiagnostics,
        ServiceStateConnector s => s.LastDiagnostics,
        _ => "",
    };

    public string? HotkeyError { get; private set; }

    public Task<SwitchResult?> SwitchToThisPcAsync() => RunAsync(c => c.ConnectAsync());

    public Task<SwitchResult?> ReleaseAsync() => RunAsync(c => c.ReleaseAsync());

    /// <summary>Re-reads binding/method/hotkey settings. Call after settings change.</summary>
    public async Task SyncAsync()
    {
        var device = _orbit.BoundDevice;
        var method = _orbit.Settings.SwitchMethod;
        if (device?.DeviceId != _boundId || method != _method)
        {
            _boundId = device?.DeviceId;
            _method = method;
            _monitor?.Dispose();
            _monitor = null;
            _coordinator = null;
            _connector = null;

            if (device?.ContainerId is { } container)
            {
                _monitor = await ConnectionMonitor.CreateAsync(device.DeviceId);
                if (_monitor is not null)
                {
                    _monitor.Changed += (_, _) => _ui.TryEnqueue(() => Changed?.Invoke(this, EventArgs.Empty));
                    _connector = method == SwitchMethods.Service
                        ? new ServiceStateConnector(device.Address, device.Name)
                        : new KsAudioConnector(container);
                    _coordinator = new SwitchCoordinator(_connector, _monitor);
                }
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        await SyncHotkeyAsync();
    }

    private string? _hotkeyText;

    private async Task SyncHotkeyAsync()
    {
        var text = _orbit.Settings.Hotkey;
        if (text == _hotkeyText)
        {
            return;
        }

        _hotkeyText = text;
        _hotkey?.Dispose();
        _hotkey = null;
        HotkeyError = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!HotkeyGesture.TryParse(text, out var gesture))
        {
            HotkeyError = $"\"{text}\" is not a valid hotkey (example: Ctrl+Alt+O).";
        }
        else
        {
            _hotkey = await GlobalHotkey.RegisterAsync(gesture!, () => _ui.TryEnqueue(async () => await SwitchToThisPcAsync()));
            if (_hotkey is null)
            {
                HotkeyError = $"{text} is already used by another app. Choose a different hotkey.";
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task CheckAutoConnectAsync()
    {
        if (!_orbit.Settings.AutoConnect || _coordinator is null)
        {
            return;
        }

        bool playing;
        try
        {
            playing = (await _media.GetCurrentAsync())?.IsPlaying == true;
        }
        catch (Exception)
        {
            return;
        }

        var nearby = _orbit.Current is not null;
        if (_autoConnect.ShouldConnect(true, playing, nearby, IsConnected, DateTimeOffset.UtcNow))
        {
            await SwitchToThisPcAsync();
        }
    }

    private async Task<SwitchResult?> RunAsync(Func<SwitchCoordinator, Task<SwitchResult>> action)
    {
        if (_coordinator is null)
        {
            LastResult = new SwitchResult(SwitchOutcome.Failed, 0, "Choose your device on the Status page first.");
            Changed?.Invoke(this, EventArgs.Empty);
            return LastResult;
        }

        IsBusy = true;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            LastResult = await action(_coordinator);
        }
        catch (Exception ex)
        {
            LastResult = new SwitchResult(SwitchOutcome.Failed, 0, $"Error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        Completed?.Invoke(this, LastResult);
        return LastResult;
    }

    public void Dispose()
    {
        _playbackTimer.Stop();
        _hotkey?.Dispose();
        _monitor?.Dispose();
    }
}
