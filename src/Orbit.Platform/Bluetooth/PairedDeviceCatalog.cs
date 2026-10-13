using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace Orbit.Platform.Bluetooth;

/// <summary>A Bluetooth Classic device paired with this PC.</summary>
/// <param name="DeviceId">Windows device-interface ID. Stored in local settings to remember "my device"; never logged.</param>
/// <param name="Address">Classic BD_ADDR. Kept in memory only (needed for connect/disconnect); never logged or persisted by Orbit.</param>
/// <param name="VendorId">Device ID profile vendor (Apple = 0x004C), or null if Windows has none.</param>
/// <param name="ProductId">Device ID profile product ID, or null.</param>
/// <param name="VendorIdSource">1 = Bluetooth SIG, 2 = USB-IF (DID spec), or null.</param>
public sealed record PairedDevice(
    string DeviceId,
    string Name,
    ulong Address,
    bool Connected,
    ushort? VendorId,
    ushort? ProductId,
    byte? VendorIdSource,
    Guid? ContainerId,
    uint ClassOfDevice)
{
    public bool IsApple => VendorId == 0x004C;
}

/// <summary>
/// Enumerates paired Bluetooth Classic devices with their Device ID profile values, the same
/// properties AirPodsDesktop uses to identify a paired model (Bluetooth_win.h L62-64, docs/sources.md S1.2).
/// </summary>
public static class PairedDeviceCatalog
{
    private const string VendorIdKey = "System.DeviceInterface.Bluetooth.VendorId";
    private const string ProductIdKey = "System.DeviceInterface.Bluetooth.ProductId";
    private const string VendorIdSourceKey = "System.DeviceInterface.Bluetooth.VendorIdSource";
    private const string ContainerIdKey = "System.Devices.ContainerId";

    // Association-endpoint form; AirPodsDesktop matches audio endpoints with this (QuickConnect_win.cpp L40).
    private const string AepContainerIdKey = "System.Devices.Aep.ContainerId";

    /// <summary>The container groups the Bluetooth device with its audio endpoints (Headphones/Headset).</summary>
    private static Guid? ContainerOf(DeviceInformation info)
    {
        foreach (var key in new[] { ContainerIdKey, AepContainerIdKey })
        {
            if (info.Properties.TryGetValue(key, out var v) && v is Guid g && g != Guid.Empty)
            {
                return g;
            }
        }

        return null;
    }

    public static async Task<IReadOnlyList<PairedDevice>> GetPairedAsync()
    {
        var infos = await DeviceInformation.FindAllAsync(
            BluetoothDevice.GetDeviceSelectorFromPairingState(true),
            [VendorIdKey, ProductIdKey, VendorIdSourceKey, ContainerIdKey, AepContainerIdKey]);

        var result = new List<PairedDevice>();
        foreach (var info in infos)
        {
            using var device = await BluetoothDevice.FromIdAsync(info.Id);
            if (device is null)
            {
                continue;
            }

            result.Add(new PairedDevice(
                info.Id,
                device.Name,
                device.BluetoothAddress,
                device.ConnectionStatus == BluetoothConnectionStatus.Connected,
                AsUShort(info.Properties, VendorIdKey),
                AsUShort(info.Properties, ProductIdKey),
                info.Properties.TryGetValue(VendorIdSourceKey, out var src) && src is byte b ? b : null,
                ContainerOf(info),
                device.ClassOfDevice.RawValue));
        }

        return result;
    }

    public static async Task<PairedDevice?> FindAsync(string deviceId) =>
        (await GetPairedAsync()).FirstOrDefault(d => d.DeviceId == deviceId);

    private static ushort? AsUShort(IReadOnlyDictionary<string, object> props, string key) =>
        props.TryGetValue(key, out var v) ? v switch
        {
            ushort u => u,
            uint u => (ushort)u,
            int i => (ushort)i,
            _ => null,
        } : null;
}
