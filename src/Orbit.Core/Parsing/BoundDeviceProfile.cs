namespace Orbit.Core.Parsing;

/// <summary>
/// Builds the profile for the user's own paired device from what Windows reports about it at runtime
/// (Device ID profile + Class of Device). Nothing about the user's device is stored in source.
/// </summary>
public static class BoundDeviceProfile
{
    public const ushort AppleVendorId = 0x004C;

    /// <summary>
    /// The Proximity Pairing model bytes are the Device ID product ID in little-endian order:
    /// product 0xAABB is advertised as d[3]=0xBB, d[4]=0xAA. Source: AirPodsDesktop matches
    /// GetProductId() against the same model table it uses for ads (AirPods.cpp L178, AppleCP.cpp L44-77)
    /// [S1.2]; confirmed on the user's AirPods Max (USB-C) on 2026-10-13 (docs/protocol-findings.md F1).
    /// </summary>
    public static ushort AdvertisedModelId(ushort productId) => (ushort)((productId & 0xFF) << 8 | productId >> 8);

    /// <summary>
    /// Bluetooth SIG Class of Device: major class Audio/Video = 0x04 (bits 12-8), minor 0x06 = Headphones
    /// (bits 7-2). Per the SIG "Assigned Numbers" baseband class-of-device tables.
    /// </summary>
    public static bool IsHeadphonesClass(uint classOfDevice) =>
        ((classOfDevice >> 8) & 0x1F) == 0x04 && ((classOfDevice >> 2) & 0x3F) == 0x06;

    /// <summary>
    /// Returns a profile for the bound device, or null if it is not an Apple Device-ID device.
    /// Name and community confidence for the model come from <see cref="ModelRegistry"/> when listed;
    /// the device's own name is used otherwise.
    /// </summary>
    public static ModelProfile? Create(string deviceName, ushort? vendorId, ushort? productId, uint classOfDevice)
    {
        if (vendorId != AppleVendorId || productId is not { } pid)
        {
            return null;
        }

        var modelId = AdvertisedModelId(pid);
        var known = ModelRegistry.Find(modelId);
        var form = IsHeadphonesClass(classOfDevice) ? DeviceForm.Headphones : known?.Form ?? DeviceForm.Earbuds;

        return new ModelProfile(
            modelId,
            known?.Name ?? deviceName,
            form,
            Confidence.VerifiedOnDevice,
            "Device ID product ID of the paired device (runtime)",
            form == DeviceForm.Headphones ? HeadphoneLayouts.Captured : null);
    }
}
