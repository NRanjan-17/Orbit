using Windows.Devices.Bluetooth;

namespace Orbit.Platform.Bluetooth;

/// <summary>This PC's Bluetooth adapter.</summary>
public static class LocalAdapter
{
    /// <summary>
    /// Lower three bytes of this PC's Bluetooth address (as a 24-bit number), or null without an adapter.
    /// Compared with the connected-host bytes in the headphones' decrypted broadcast (protocol-findings F11).
    /// Kept in memory only; never logged or stored.
    /// </summary>
    public static async Task<int?> GetAddressSuffixAsync()
    {
        var adapter = await BluetoothAdapter.GetDefaultAsync();
        return adapter is null ? null : (int)(adapter.BluetoothAddress & 0xFFFFFF);
    }
}
