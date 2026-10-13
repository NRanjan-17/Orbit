using Windows.Devices.Enumeration;

namespace Orbit.Platform.Audio;

/// <summary>
/// Watches for Apple headphones connected by USB-C cable. With the cable plugged in, the AirPods Max (USB-C) appears
/// as a USB composite device from Apple's USB vendor ID 0x05AC with a USB Audio interface (owner's PC, 2026-10-14:
/// "AirPods Max USB Audio" in the Sound, video and game controllers class). Matching on the vendor ID and the media
/// device class (not a product ID) keeps this independent of the exact model.
/// </summary>
public sealed class UsbAudioMonitor : IDisposable
{
    // Device instance IDs start with "USB\VID_05AC&" for Apple USB devices; the media class GUID is the standard
    // "Sound, video and game controllers" setup class.
    private const string Selector =
        "System.Devices.DeviceInstanceId:~<\"USB\\VID_05AC&\" AND " +
        "System.Devices.ClassGuid:=\"{4d36e96c-e325-11ce-bfc1-08002be10318}\" AND " +
        "System.Devices.Present:=System.StructuredQueryType.Boolean#True";

    private readonly DeviceWatcher _watcher;
    private readonly Dictionary<string, string> _present = [];
    private readonly object _gate = new();

    public UsbAudioMonitor()
    {
        _watcher = DeviceInformation.CreateWatcher(Selector, [], DeviceInformationKind.Device);
        _watcher.Added += (_, info) => Update(() => _present[info.Id] = info.Name);
        _watcher.Removed += (_, update) => Update(() => _present.Remove(update.Id));
        _watcher.Start();
    }

    /// <summary>Raised (on a background thread) when a cable-connected Apple audio device appears or disappears.</summary>
    public event EventHandler? Changed;

    public bool IsConnected
    {
        get { lock (_gate) { return _present.Count > 0; } }
    }

    /// <summary>Name Windows shows for the USB audio interface, e.g. "AirPods Max USB Audio".</summary>
    public string? DeviceName
    {
        get { lock (_gate) { return _present.Values.FirstOrDefault(); } }
    }

    private void Update(Action change)
    {
        lock (_gate)
        {
            change();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            _watcher.Stop();
        }
    }
}
