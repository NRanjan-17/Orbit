namespace Orbit.Core.Parsing;

/// <summary>Decoded state of one Proximity Pairing frame. Unknown fields are <see cref="Reading{T}.Unknown"/>.</summary>
public sealed record DeviceStatus
{
    public required ProximityPairingFrame Frame { get; init; }

    /// <summary>Null when the model ID is not in <see cref="ModelRegistry"/>.</summary>
    public ModelProfile? Profile { get; init; }

    public ushort ModelId => Frame.ModelId;

    public string ModelName => Profile?.Name ?? $"Unknown Apple audio device (0x{ModelId:X4})";

    public DeviceForm? Form => Profile?.Form;

    // ---- Headphones (single battery) ----
    public Reading<int> Battery { get; init; } = Reading<int>.Unknown();

    /// <summary>
    /// Exact battery percent decrypted from the encrypted tail with the user's key (Crypto.ExactBatteryResolver).
    /// Unknown when no key is set or the key is not yet confirmed.
    /// </summary>
    public Reading<int> ExactBattery { get; init; } = Reading<int>.Unknown();

    /// <summary>Estimate within the advertised 10 % step (no key needed); see Tracking.BatteryEstimator.</summary>
    public Tracking.BatteryEstimate? Estimate { get; init; }

    /// <summary>
    /// Lower three bytes of the address of the device the headphones are connected to, from the decrypted tail
    /// (needs the exact-battery key). Compare with this PC's adapter to tell "this PC" from "another device".
    /// </summary>
    public Reading<int> ConnectedHostSuffix { get; init; } = Reading<int>.Unknown();
    public Reading<bool> Charging { get; init; } = Reading<bool>.Unknown();
    public Reading<bool> Worn { get; init; } = Reading<bool>.Unknown();

    // ---- Earbuds ----
    public Reading<int> LeftBattery { get; init; } = Reading<int>.Unknown();
    public Reading<int> RightBattery { get; init; } = Reading<int>.Unknown();
    public Reading<int> CaseBattery { get; init; } = Reading<int>.Unknown();
    public Reading<bool> LeftCharging { get; init; } = Reading<bool>.Unknown();
    public Reading<bool> RightCharging { get; init; } = Reading<bool>.Unknown();
    public Reading<bool> CaseCharging { get; init; } = Reading<bool>.Unknown();
    public Reading<bool> LeftInEar { get; init; } = Reading<bool>.Unknown();
    public Reading<bool> RightInEar { get; init; } = Reading<bool>.Unknown();
    public Reading<bool> LidOpen { get; init; } = Reading<bool>.Unknown();

    // ---- Common ----
    public Reading<string> Color { get; init; } = Reading<string>.Unknown();

    /// <summary>LibrePods' reading of d[10] (single source).</summary>
    public Reading<string> ConnectionState { get; init; } = Reading<string>.Unknown();

    /// <summary>
    /// "Is it being worn" in a form-independent way: headphones → <see cref="Worn"/>;
    /// earbuds → true if either bud is in an ear, false if both are known to be out.
    /// </summary>
    public Reading<bool> AnyWorn
    {
        get
        {
            if (Form == DeviceForm.Headphones)
            {
                return Worn;
            }

            if (!LeftInEar.HasValue || !RightInEar.HasValue)
            {
                return Reading<bool>.Unknown("in-ear unknown");
            }

            var conf = (Confidence)Math.Min((int)LeftInEar.Confidence, (int)RightInEar.Confidence);
            return new Reading<bool>(LeftInEar.Value || RightInEar.Value, conf, LeftInEar.Source);
        }
    }

    /// <summary>Lowest known battery, for tray tooltips and low-battery warnings. Case excluded.</summary>
    public Reading<int> LowestBattery
    {
        get
        {
            if (Form == DeviceForm.Headphones)
            {
                return Battery;
            }

            var buds = new[] { LeftBattery, RightBattery }.Where(r => r.HasValue).ToList();
            return buds.Count == 0 ? Reading<int>.Unknown() : buds.MinBy(r => r.Value);
        }
    }
}
