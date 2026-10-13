namespace Orbit.Core.Capture;

/// <summary>
/// One received Bluetooth LE advertisement carrying manufacturer data,
/// or a user marker row (when <see cref="Kind"/> is <see cref="RecordKind.Marker"/>).
/// </summary>
/// <param name="TimestampUtc">Time the advertisement was received.</param>
/// <param name="Address">Bluetooth address as reported by Windows. Apple devices rotate this.</param>
/// <param name="Rssi">Signal strength in dBm.</param>
/// <param name="AdvertisementType">WinRT BluetoothLEAdvertisementType name (e.g. ConnectableUndirected).</param>
/// <param name="CompanyId">Manufacturer company ID; Apple is 0x004C.</param>
/// <param name="Payload">Manufacturer data bytes after the 2-byte company ID.</param>
/// <param name="Tag">Most recent user tag at the time of reception (the state being captured).</param>
/// <param name="Note">Free text (marker text, or empty).</param>
public sealed record AdvertisementRecord(
    RecordKind Kind,
    DateTimeOffset TimestampUtc,
    ulong Address,
    short Rssi,
    string AdvertisementType,
    ushort CompanyId,
    byte[] Payload,
    string Tag,
    string Note)
{
    public const ushort AppleCompanyId = 0x004C;

    /// <summary>First payload byte = Apple Continuity message type (0x07 = Proximity Pairing).</summary>
    public byte? AppleMessageType => CompanyId == AppleCompanyId && Payload.Length > 0 ? Payload[0] : null;

    public string PayloadHex => Hex.Format(Payload);

    public string AddressText => FormatAddress(Address);

    public static string FormatAddress(ulong address) =>
        string.Join(':', Enumerable.Range(0, 6).Select(i => ((address >> (8 * (5 - i))) & 0xFF).ToString("X2")));

    public static AdvertisementRecord Marker(DateTimeOffset timestampUtc, string tag, string note = "") =>
        new(RecordKind.Marker, timestampUtc, 0, 0, "", 0, [], tag, note);
}

public enum RecordKind
{
    Advertisement,
    Marker,
}
