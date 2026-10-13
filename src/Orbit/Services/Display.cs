using Orbit.Core.Parsing;
using Orbit.Core.Tracking;

namespace Orbit.Services;

/// <summary>
/// Turns readings into UI text, applying the verification policy: verified values are shown plainly,
/// corroborated values get "(unverified)", single-source values are shown only when the user opts in.
/// </summary>
public static class Display
{
    public static bool IsShown<T>(Reading<T> r, bool showUnverified) =>
        r.HasValue && (r.Confidence >= Confidence.Corroborated || showUnverified);

    // Values are shown without inline "(unverified)" text (owner's request, 2026-10-14). Unverified values are
    // instead flagged by an info icon + tooltip next to the battery (see IsBatteryUnverified) and in the README.
    private static string Badge(Confidence c) => "";

    /// <summary>True when the battery value being displayed is not yet verified on the owner's device.</summary>
    public static bool IsBatteryUnverified(DeviceStatus s) =>
        s.ExactBattery.HasValue
            ? s.ExactBattery.Confidence < Confidence.VerifiedOnDevice
            : s.LowestBattery.HasValue && s.LowestBattery.Confidence < Confidence.VerifiedOnDevice;

    public static string Battery(Reading<int> r, bool showUnverified) =>
        IsShown(r, showUnverified) ? ProximityPairingParser.FormatBattery(r.Value) + Badge(r.Confidence) : "Unknown";

    /// <summary>Exact percent when decrypted with the user's key, otherwise the advertised 10 % range.</summary>
    public static string BatteryOf(DeviceStatus s, bool showUnverified)
    {
        if (s.ExactBattery.HasValue)
        {
            return $"{s.ExactBattery.Value}%" + Badge(s.ExactBattery.Confidence);
        }

        return Battery(s.LowestBattery, showUnverified);
    }

    public static string Flag(Reading<bool> r, bool showUnverified, string yes, string no) =>
        IsShown(r, showUnverified) ? (r.Value ? yes : no) + Badge(r.Confidence) : "Unknown";

    public static string Signal(TrackedDevice d) => $"{d.LastRssi} dBm";

    /// <summary>One line for the tray tooltip (max 127 chars on Windows).</summary>
    public static string Tooltip(string name, TrackedDevice? current, TrackedDevice? lastKnown, bool showUnverified)
    {
        if (current is null)
        {
            return lastKnown is null
                ? $"Orbit: {name} not seen"
                : $"Orbit: {name} asleep or out of range (last {BatteryOf(lastKnown.Status, showUnverified)})";
        }

        var s = current.Status;
        var worn = s.AnyWorn.HasValue ? (s.AnyWorn.Value ? ", on head" : ", off head") : "";
        var text = $"Orbit: {name} {BatteryOf(s, showUnverified)}{worn}";
        return text.Length > 127 ? text[..127] : text;
    }
}
