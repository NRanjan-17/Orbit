namespace Orbit.Core.Parsing;

/// <summary>
/// Structural split of an Apple Proximity Pairing (type 0x07) payload into named bytes, with no
/// interpretation. Indices are into the manufacturer data after the company ID.
/// Framing (type 0x07, length 0x19, 27 bytes, 16-byte changing tail) is corroborated by
/// docs/sources.md S1.1 and observed in our probe capture of the Max (USB-C).
/// </summary>
public sealed record ProximityPairingFrame(
    byte Prefix,
    byte ModelHi,
    byte ModelLo,
    byte Status,
    byte Battery,
    byte ChargeAndCase,
    byte Lid,
    byte Color,
    byte Byte10,
    byte[] EncryptedTail)
{
    public const byte MessageType = 0x07;
    public const byte ExpectedLength = 0x19;
    public const int TotalLength = 27;
    public const int EncryptedTailLength = 16;

    /// <summary>Model ID as on-air bytes d[3]&lt;&lt;8 | d[4], e.g. 0x1F20 (LibrePods notation; AirPodsDesktop writes 0x201F).</summary>
    public ushort ModelId => (ushort)(ModelHi << 8 | ModelLo);

    /// <summary>
    /// The unencrypted, non-rotating part used to recognise "the same device" across rotating addresses:
    /// model and colour. Status/battery are excluded because they change with state.
    /// </summary>
    public string IdentityKey => $"{ModelId:X4}-{Color:X2}";

    public static bool TryParse(ReadOnlySpan<byte> payload, out ProximityPairingFrame? frame)
    {
        frame = null;
        if (payload.Length != TotalLength || payload[0] != MessageType || payload[1] != ExpectedLength)
        {
            return false;
        }

        frame = new ProximityPairingFrame(
            payload[2], payload[3], payload[4], payload[5], payload[6], payload[7], payload[8], payload[9], payload[10],
            payload[11..].ToArray());
        return true;
    }
}
