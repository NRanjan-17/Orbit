using System.Runtime.InteropServices.WindowsRuntime;
using Orbit.Core.Capture;
using Windows.Devices.Bluetooth.Advertisement;

namespace Orbit.Platform.Bluetooth;

/// <summary>A raw Apple manufacturer-data section from one received advertisement.</summary>
/// <param name="Payload">Bytes after the 2-byte company ID (Windows strips the company ID).</param>
public sealed record RawAppleAdvertisement(
    DateTimeOffset TimestampUtc,
    ulong Address,
    short Rssi,
    string AdvertisementType,
    byte[] Payload);

/// <summary>
/// Wraps <see cref="BluetoothLEAdvertisementWatcher"/> and raises one event per Apple
/// (company ID 0x004C) manufacturer-data section. No parsing happens here.
/// </summary>
public sealed class AppleAdvertisementScanner : IDisposable
{
    private readonly bool _activeScanning;
    private BluetoothLEAdvertisementWatcher _watcher;
    private volatile bool _stopRequested;

    public AppleAdvertisementScanner(bool activeScanning = true)
    {
        _activeScanning = activeScanning;
        _watcher = CreateWatcher();
    }

    private BluetoothLEAdvertisementWatcher CreateWatcher()
    {
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            // Active scanning also yields scan-response packets; harmless and useful when capturing.
            ScanningMode = _activeScanning ? BluetoothLEScanningMode.Active : BluetoothLEScanningMode.Passive,
        };

        // Report every advertisement rather than letting Windows sample them by signal strength; auto-pause
        // latency depends on seeing the first frame after the headphones change state.
        watcher.SignalStrengthFilter.SamplingInterval = TimeSpan.Zero;
        watcher.Received += OnReceived;
        watcher.Stopped += OnStopped;
        return watcher;
    }

    public event EventHandler<RawAppleAdvertisement>? AdvertisementReceived;

    /// <summary>Raised when Windows stops the watcher (radio off, adapter missing, access denied).</summary>
    public event EventHandler<string>? Stopped;

    public bool IsRunning => _watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started;

    public string Status => _watcher.Status.ToString();

    public void Start()
    {
        if (_watcher.Status is not BluetoothLEAdvertisementWatcherStatus.Started)
        {
            _watcher.Start();
        }
    }

    /// <summary>
    /// Starts scanning again after Windows stopped it (radio turned off and on, adapter re-plugged). An aborted
    /// watcher is replaced, because Windows doesn't reliably restart one.
    /// </summary>
    public void Restart()
    {
        if (_watcher.Status is BluetoothLEAdvertisementWatcherStatus.Aborted or BluetoothLEAdvertisementWatcherStatus.Stopped)
        {
            _watcher.Received -= OnReceived;
            _watcher.Stopped -= OnStopped;
            _watcher = CreateWatcher();
        }

        Start();
    }

    public void Stop()
    {
        if (_watcher.Status is BluetoothLEAdvertisementWatcherStatus.Started or BluetoothLEAdvertisementWatcherStatus.Created)
        {
            _stopRequested = true;
            _watcher.Stop();
        }
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        foreach (var section in args.Advertisement.ManufacturerData)
        {
            if (section.CompanyId != AdvertisementRecord.AppleCompanyId)
            {
                continue;
            }

            AdvertisementReceived?.Invoke(this, new RawAppleAdvertisement(
                args.Timestamp.ToUniversalTime(),
                args.BluetoothAddress,
                args.RawSignalStrengthInDBm,
                args.AdvertisementType.ToString(),
                section.Data.ToArray()));
        }
    }

    private void OnStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        // Only report stops we did not ask for (radio turned off, adapter removed, ...).
        if (_stopRequested)
        {
            _stopRequested = false;
            return;
        }

        Stopped?.Invoke(this, args.Error.ToString());
    }

    public void Dispose()
    {
        _watcher.Received -= OnReceived;
        _watcher.Stopped -= OnStopped;
        Stop();
    }
}
