using Orbit.Core.Switching;
using Windows.Devices.Bluetooth;

namespace Orbit.Platform.Bluetooth;

/// <summary>Tracks whether the paired device has a Bluetooth Classic connection to this PC (WinRT ConnectionStatus).</summary>
public sealed class ConnectionMonitor : IConnectionState, IDisposable
{
    private readonly BluetoothDevice _device;

    private ConnectionMonitor(BluetoothDevice device)
    {
        _device = device;
        _device.ConnectionStatusChanged += OnChanged;
    }

    public event EventHandler<bool>? Changed;

    public bool IsConnected => _device.ConnectionStatus == BluetoothConnectionStatus.Connected;

    public static async Task<ConnectionMonitor?> CreateAsync(string deviceId) =>
        await BluetoothDevice.FromIdAsync(deviceId) is { } device ? new ConnectionMonitor(device) : null;

    private void OnChanged(BluetoothDevice sender, object args) => Changed?.Invoke(this, IsConnected);

    public void Dispose()
    {
        _device.ConnectionStatusChanged -= OnChanged;
        _device.Dispose();
    }
}
