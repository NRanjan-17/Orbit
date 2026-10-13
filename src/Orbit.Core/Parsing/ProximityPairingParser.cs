namespace Orbit.Core.Parsing;

/// <summary>
/// Decodes Apple Proximity Pairing (type 0x07) payloads. Citations refer to docs/sources.md.
/// Fields carry a <see cref="Confidence"/>; callers decide what is safe to show or act on.
/// </summary>
public static class ProximityPairingParser
{
    // Colour table: furiousMAC airpods_color_vals L1447-1461 = AirPodsDesktop AppleCP.h L46-60 = LibrePods BLEManager.kt L101-106 [S1.5].
    private static readonly string[] Colors =
    [
        "White", "Black", "Red", "Blue", "Pink", "Gray", "Silver", "Gold", "Rose Gold", "Space Gray", "Dark Blue", "Light Blue", "Yellow",
    ];

    // d[10] connection state: LibrePods only (BLEManager.kt L108-111, linux blemanager.h L52-61) [S1.5].
    private static readonly Dictionary<byte, string> ConnectionStates = new()
    {
        [0x00] = "Disconnected", [0x04] = "Idle", [0x05] = "Music", [0x06] = "Call", [0x07] = "Ringing", [0x09] = "Hanging up",
    };

    private const string EarbudSources = "LibrePods BLEManager.kt L433-470 / linux blemanager.cpp L146-212; AirPodsDesktop AppleCP.cpp; OpenPods PodsStatus.java [S1.3-S1.4]";

    /// <summary>
    /// Parses a payload (manufacturer data after the company ID). Returns null when it is not a
    /// 27-byte type-0x07 frame, or when d[2] == 0x00 (pairing mode; LibrePods linux blemanager.cpp
    /// L139-142 skips these because "the values are differently structured").
    /// </summary>
    public static DeviceStatus? Parse(ReadOnlySpan<byte> payload, Func<ushort, ModelProfile?>? lookup = null)
    {
        if (!ProximityPairingFrame.TryParse(payload, out var frame) || frame is null || frame.Prefix == 0x00)
        {
            return null;
        }

        var profile = (lookup ?? ModelRegistry.Find)(frame.ModelId);
        var status = new DeviceStatus
        {
            Frame = frame,
            Profile = profile,
            Color = frame.Color < Colors.Length
                ? new Reading<string>(Colors[frame.Color], Confidence.Corroborated, "furiousMAC, AirPodsDesktop, LibrePods [S1.5]")
                : Reading<string>.Unknown($"colour byte 0x{frame.Color:X2} not in any table"),
            ConnectionState = ConnectionStates.TryGetValue(frame.Byte10, out var cs)
                ? new Reading<string>(cs, Confidence.SingleSource, "LibrePods d[10] enum [S1.5]")
                : Reading<string>.Unknown($"d[10]=0x{frame.Byte10:X2} not in LibrePods enum"),
        };

        return profile?.Form switch
        {
            DeviceForm.Headphones => DecodeHeadphones(status, payload, profile.Headphone),
            DeviceForm.Earbuds => DecodeEarbuds(status, frame),
            // Unknown model: structure is known but field meaning is not -> everything stays unknown.
            _ => status,
        };
    }

    /// <summary>Battery nibble → percent. 0-10 = ×10 %. 0xF = unknown (OpenPods, LibrePods, AirPodsDesktop).
    /// 0xB-0xE: sources disagree (LibrePods Android clamps to 100, AirPodsDesktop/OpenPods reject) → unknown.</summary>
    internal static int? NibbleToPercent(int nibble) => nibble is >= 0 and <= 10 ? nibble * 10 : null;

    /// <summary>
    /// The advertisement only carries 10 % steps (Mac 37 % was advertised as 3), so show the range the
    /// value stands for instead of a false-precision number: 30 → "30–39%", 100 → "100%".
    /// </summary>
    public static string FormatBattery(int percent) => percent >= 100 ? "100%" : $"{percent}–{percent + 9}%";

    private static DeviceStatus DecodeHeadphones(DeviceStatus status, ReadOnlySpan<byte> payload, HeadphoneLayout? layout)
    {
        if (layout is null)
        {
            return status;
        }

        var battery = Reading<int>.Unknown("no battery rule for this model");
        if (layout.Battery is { } br && NibbleToPercent(br.Read(status.Frame, payload)) is { } pct)
        {
            battery = new Reading<int>(pct, br.Confidence, br.Source);
        }

        return status with
        {
            Battery = battery,
            Charging = ReadBool(layout.Charging, payload, "charging"),
            Worn = ReadBool(layout.Worn, payload, "on-head"),
        };
    }

    private static Reading<bool> ReadBool(IBoolRule? rule, ReadOnlySpan<byte> payload, string what)
    {
        if (rule is null)
        {
            return Reading<bool>.Unknown($"no {what} rule");
        }

        return rule.Read(payload) is { } v
            ? new Reading<bool>(v, rule.Confidence, rule.Source)
            : Reading<bool>.Unknown($"{what}: byte value not seen in captures");
    }

    private static DeviceStatus DecodeEarbuds(DeviceStatus status, ProximityPairingFrame f)
    {
        // Status bit 0x20: broadcasting pod is the left one (AirPodsDesktop broadcastFrom, LibrePods primaryLeft, OpenPods flip).
        var primaryLeft = (f.Status & 0x20) != 0;
        var flipped = !primaryLeft;

        // Battery: low nibble = left unless flipped (LibrePods BLEManager.kt: leftBatteryNibble = isFlipped ? hi : lo; same in APD/OP).
        var lo = f.Battery & 0x0F;
        var hi = f.Battery >> 4;
        var left = NibbleToPercent(flipped ? hi : lo);
        var right = NibbleToPercent(flipped ? lo : hi);
        var caseBattery = NibbleToPercent(f.ChargeAndCase & 0x0F);

        // Charging flags: high nibble of d[7]; 0x1 = primary pod, 0x2 = other pod, 0x4 = case (all four sources).
        var flags = f.ChargeAndCase >> 4;
        var leftCharging = flipped ? (flags & 0x02) != 0 : (flags & 0x01) != 0;
        var rightCharging = flipped ? (flags & 0x01) != 0 : (flags & 0x02) != 0;
        var caseCharging = (flags & 0x04) != 0;

        // In-ear. LibrePods Linux (blemanager.cpp L192): xor = flipped ^ thisPodInCase(0x40); left = xor ? 0x08 : 0x02.
        // OpenPods and AirPodsDesktop agree with this for the common case. NOTE: LibrePods *Android* uses the
        // opposite xor; we follow the 3-project majority and keep confidence at Corroborated (not verified).
        var thisInCase = (f.Status & 0x40) != 0;
        var xor = flipped ^ thisInCase;
        var leftInEar = (f.Status & (xor ? 0x08 : 0x02)) != 0;
        var rightInEar = (f.Status & (xor ? 0x02 : 0x08)) != 0;

        // Lid: bit 3 of d[8] = closed (AirPodsDesktop AppleCP.cpp L138, LibrePods). LibrePods only trusts it when a pod is in the case.
        var lidOpen = thisInCase
            ? new Reading<bool>((f.Lid & 0x08) == 0, Confidence.Corroborated, "AirPodsDesktop AppleCP.cpp L138; LibrePods blemanager.cpp L211-212 [S1.5]")
            : Reading<bool>.Unknown("lid only reported while a pod is in the case");

        return status with
        {
            LeftBattery = Pct(left),
            RightBattery = Pct(right),
            CaseBattery = Pct(caseBattery),
            LeftCharging = Flag(leftCharging),
            RightCharging = Flag(rightCharging),
            CaseCharging = Flag(caseCharging),
            LeftInEar = Flag(leftInEar),
            RightInEar = Flag(rightInEar),
            LidOpen = lidOpen,
        };

        static Reading<int> Pct(int? v) => v is { } p ? new Reading<int>(p, Confidence.Corroborated, EarbudSources) : Reading<int>.Unknown("0xF or out-of-range nibble");
        static Reading<bool> Flag(bool v) => new(v, Confidence.Corroborated, EarbudSources);
    }
}
