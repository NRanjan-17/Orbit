using Orbit.Core.Parsing;

namespace Orbit.Core.Tracking;

/// <summary>
/// Raises a low-battery warning once when the battery drops to the threshold, and re-arms only after it has
/// risen 10 points above it (or charging was seen), so the toast does not repeat on every advertisement.
/// Only readings at or above <see cref="MinimumConfidence"/> count.
/// </summary>
public sealed class LowBatteryMonitor(int thresholdPercent)
{
    private bool _warned;

    public int ThresholdPercent { get; set; } = thresholdPercent;

    public Confidence MinimumConfidence { get; set; } = Confidence.VerifiedOnDevice;

    /// <summary>Returns the battery percent when a warning should be shown now, otherwise null.</summary>
    public int? Check(Reading<int> battery, Reading<bool> charging)
    {
        if (!battery.IsUsable(MinimumConfidence))
        {
            return null;
        }

        if (charging.IsUsable(MinimumConfidence) && charging.Value)
        {
            _warned = false;
            return null;
        }

        if (battery.Value >= ThresholdPercent + 10)
        {
            _warned = false;
            return null;
        }

        if (!_warned && battery.Value <= ThresholdPercent)
        {
            _warned = true;
            return battery.Value;
        }

        return null;
    }
}
