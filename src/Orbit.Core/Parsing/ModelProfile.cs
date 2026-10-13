namespace Orbit.Core.Parsing;

public enum DeviceForm
{
    /// <summary>One battery, no case battery (e.g. AirPods Max).</summary>
    Headphones,

    /// <summary>Left/right buds plus a charging case.</summary>
    Earbuds,
}

/// <summary>A 4-bit field inside the Proximity Pairing payload.</summary>
/// <param name="Offset">Index into the payload after the company ID.</param>
/// <param name="High">True for bits 7-4, false for bits 3-0.</param>
public sealed record NibbleRule(int Offset, bool High, Confidence Confidence, string Source)
{
    public int Read(ProximityPairingFrame f, ReadOnlySpan<byte> payload) => High ? payload[Offset] >> 4 : payload[Offset] & 0x0F;
}

/// <summary>A boolean field. <see cref="Read"/> returns null when the payload does not determine it.</summary>
public interface IBoolRule
{
    Confidence Confidence { get; }

    string Source { get; }

    bool? Read(ReadOnlySpan<byte> payload);
}

/// <summary>A boolean flag inside the payload.</summary>
/// <param name="Mask">Bit mask within the byte at <paramref name="Offset"/>.</param>
/// <param name="TrueWhenSet">Whether a set bit means true (e.g. "charging") or false.</param>
public sealed record BitRule(int Offset, byte Mask, bool TrueWhenSet, Confidence Confidence, string Source) : IBoolRule
{
    public bool? Read(ReadOnlySpan<byte> payload) => ((payload[Offset] & Mask) != 0) == TrueWhenSet;
}

/// <summary>
/// A boolean decided by exact byte values: only values seen in tagged captures map to true/false;
/// any other value yields null (unknown). Used where bit masks are ambiguous.
/// </summary>
public sealed record ByteValueRule(int Offset, byte[] TrueValues, byte[] FalseValues, Confidence Confidence, string Source) : IBoolRule
{
    public bool? Read(ReadOnlySpan<byte> payload) =>
        TrueValues.Contains(payload[Offset]) ? true : FalseValues.Contains(payload[Offset]) ? false : null;
}

/// <summary>
/// Field positions for a single-battery headphone. A null rule means the field is unknown for this
/// model and will be reported as unknown.
/// </summary>
public sealed record HeadphoneLayout(NibbleRule? Battery, IBoolRule? Charging, IBoolRule? Worn);

/// <summary>Everything Orbit knows about one model, and how sure it is.</summary>
/// <param name="ModelId">On-air bytes d[3]&lt;&lt;8|d[4] (e.g. 0x1F20).</param>
public sealed record ModelProfile(
    ushort ModelId,
    string Name,
    DeviceForm Form,
    Confidence IdConfidence,
    string IdSource,
    HeadphoneLayout? Headphone = null)
{
    public bool IsHeadphone => Form == DeviceForm.Headphones;
}
