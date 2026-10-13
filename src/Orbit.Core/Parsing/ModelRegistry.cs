namespace Orbit.Core.Parsing;

/// <summary>
/// Community-documented models (LibrePods and others, docs/sources.md S1.2), used to name and decode
/// devices that are NOT the user's bound device. The user's own device is described at runtime by
/// <see cref="BoundDeviceProfile"/> from the Device ID that Windows reports for it, so no
/// device-specific identity lives in source.
/// Do not add IDs or field positions without a citation.
/// </summary>
public static class ModelRegistry
{
    public static IReadOnlyList<ModelProfile> All { get; } =
    [
        // Single-battery headphones. Battery/charging rules are OpenPods-only (SingleSource) [S1.4].
        new(0x0A20, "AirPods Max (Lightning)", DeviceForm.Headphones, Confidence.Corroborated,
            "LibrePods BLEManager.kt L88-99; AirPodsDesktop AppleCP.cpp L44-77 [S1.2]", HeadphoneLayouts.Community),

        Earbuds(0x0220, "AirPods (1st gen)", "furiousMAC, OpenPods, LibrePods, AirPodsDesktop"),
        Earbuds(0x0F20, "AirPods (2nd gen)", "furiousMAC, OpenPods, LibrePods, AirPodsDesktop"),
        Earbuds(0x1320, "AirPods (3rd gen)", "OpenPods, LibrePods, AirPodsDesktop"),
        Earbuds(0x1920, "AirPods 4", "LibrePods, AirPodsDesktop"),
        Earbuds(0x1B20, "AirPods 4 (ANC)", "LibrePods, AirPodsDesktop"),
        Earbuds(0x0E20, "AirPods Pro", "furiousMAC, OpenPods, LibrePods, AirPodsDesktop"),
        Earbuds(0x1420, "AirPods Pro 2 (Lightning)", "OpenPods, LibrePods, AirPodsDesktop"),
        Earbuds(0x2420, "AirPods Pro 2 (USB-C)", "OpenPods, LibrePods, AirPodsDesktop"),
        Earbuds(0x2720, "AirPods Pro 3", "OpenPods, AirPodsDesktop"),
        // AirPodsDesktop only (AppleCP.cpp + sanitised captures in Tests/AirPodsDomainTests.cpp L203-224).
        new(0x3020, "AirPods 5", DeviceForm.Earbuds, Confidence.SingleSource, "AirPodsDesktop [S1.2]"),
        new(0x3620, "AirPods 5", DeviceForm.Earbuds, Confidence.SingleSource, "AirPodsDesktop [S1.2]"),
    ];

    private static readonly Dictionary<ushort, ModelProfile> ById = All.ToDictionary(p => p.ModelId);

    public static ModelProfile? Find(ushort modelId) => ById.GetValueOrDefault(modelId);

    /// <summary>Looks up the bound device first, then the community table.</summary>
    public static Func<ushort, ModelProfile?> WithBound(ModelProfile? bound) =>
        id => bound is not null && bound.ModelId == id ? bound : Find(id);

    /// <summary>Returns a copy of <paramref name="profile"/> with a different headphone layout.</summary>
    public static ModelProfile WithLayout(ModelProfile profile, HeadphoneLayout layout) => profile with { Headphone = layout };

    private static ModelProfile Earbuds(ushort id, string name, string sources) =>
        new(id, name, DeviceForm.Earbuds, Confidence.Corroborated, sources + " [S1.2]");
}

/// <summary>Field rules for single-battery Apple headphones in the Proximity Pairing frame.</summary>
public static class HeadphoneLayouts
{
    /// <summary>
    /// What the community documents (OpenPods only) — UNVERIFIED. On-head: no source [S1.3] → unknown.
    /// Replaced by capture-verified rules in docs/protocol-findings.md once the tagged session is analysed.
    /// </summary>
    public static HeadphoneLayout Community { get; } = new(
        Battery: new NibbleRule(6, High: false, Confidence.SingleSource, "OpenPods PodsStatus.java L53 (low nibble of d[6]) [S1.4]"),
        Charging: new BitRule(7, 0x10, TrueWhenSet: true, Confidence.SingleSource, "OpenPods PodsStatus.java L60 (d[7] & 0x10) [S1.4]"),
        Worn: null);
}
