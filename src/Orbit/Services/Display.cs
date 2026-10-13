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

    /// <summary>Settings → "Estimate battery between 10% steps" (no key needed).</summary>
    public static bool EstimateBattery { get; set; }

    public static string Battery(Reading<int> r, bool showUnverified) =>
        IsShown(r, showUnverified) ? ProximityPairingParser.FormatBattery(r.Value) + Badge(r.Confidence) : "Unknown";

    /// <summary>The battery number to show, or null for a range: exact (key) first, then the estimate if enabled.</summary>
    public static int? BatteryNumber(DeviceStatus s) =>
        s.ExactBattery.HasValue ? s.ExactBattery.Value
        : EstimateBattery && s.Estimate is { } e ? e.Percent
        : null;

    /// <summary>True when the shown battery is the estimate (so the UI can say it may be a few % off).</summary>
    public static bool IsEstimated(DeviceStatus s) => !s.ExactBattery.HasValue && EstimateBattery && s.Estimate is not null;

    /// <summary>Exact percent with a key; "≈66%" when estimating; otherwise the advertised 10 % range.</summary>
    public static string BatteryOf(DeviceStatus s, bool showUnverified)
    {
        if (s.ExactBattery.HasValue)
        {
            return $"{s.ExactBattery.Value}%" + Badge(s.ExactBattery.Confidence);
        }

        if (IsEstimated(s))
        {
            return $"≈{s.Estimate!.Percent}%";
        }

        return Battery(s.LowestBattery, showUnverified);
    }

    /// <summary>Tooltip for the battery info icon, or null when the value needs no explanation.</summary>
    public static string? BatteryHint(DeviceStatus s) =>
        IsEstimated(s)
            ? s.Estimate!.Anchored
                ? "Estimated: the headphones report battery in 10% steps. Orbit counts within the step from when it last changed and how fast it changes, so it may be a few percent off."
                : "Estimated: the middle of the 10% step the headphones report. It gets more accurate after the next step change."
            : IsBatteryUnverified(s)
                ? "This value is decoded the way open-source projects describe, but it hasn't been confirmed against your Mac yet."
                : null;

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
