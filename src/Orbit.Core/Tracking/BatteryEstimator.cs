namespace Orbit.Core.Tracking;

/// <summary>An estimated battery level inside the advertised 10 % step.</summary>
/// <param name="Percent">Estimated percent, always within the current step [lower, lower + 9].</param>
/// <param name="Anchored">True once a step change has been seen (the estimate is then extrapolated from a known
/// boundary); false means only the step's midpoint is known.</param>
public sealed record BatteryEstimate(int Percent, bool Anchored);

/// <summary>
/// Estimates the battery between the 10 % steps the headphones advertise, without the exact-battery key.
/// When the step changes, the battery is known to be at that step's boundary at that moment (down from 70s into
/// 60s → 69 %; up from 50s into 60s while charging → 60 %, using the verified floor rule: 37 % ↔ 3, 65 % ↔ 6).
/// From the time between successive step changes it learns the drain (or charge) rate and extrapolates within
/// the current step, clamped to it — so the estimate can never leave the band the headphones report.
/// Before any step change it returns the midpoint, marked not anchored.
/// </summary>
public sealed class BatteryEstimator
{
    /// <summary>Assumed rates until two step changes have been observed (AirPods Max: roughly 20 h listening;
    /// a few % per hour drain, much faster when charging). Used only to extrapolate within one step.</summary>
    public static readonly double DefaultDrainPerHour = 5.0;
    public static readonly double DefaultChargePerHour = 40.0;

    private int? _step;              // current step lower bound (0, 10, ... 100)
    private DateTimeOffset? _anchorAt;
    private int _anchorPercent;
    private bool _anchorRising;
    private DateTimeOffset? _previousChangeAt;
    private double? _learnedPerHour;   // learned rate in % per hour for the current direction
    private bool? _learnedRising;

    /// <param name="stepLowerBound">Advertised step (lower bound, e.g. 60 for "60-69 %").</param>
    /// <param name="charging">Whether the headphones report charging (null if unknown).</param>
    public BatteryEstimate Update(int stepLowerBound, bool? charging, DateTimeOffset at)
    {
        if (_step is { } previous && previous != stepLowerBound)
        {
            var rising = stepLowerBound > previous;

            // Learn the rate from consecutive same-direction single-step changes.
            if (_previousChangeAt is { } prevAt && Math.Abs(stepLowerBound - previous) == 10 && _anchorAt is not null && _anchorRising == rising)
            {
                var hours = (at - prevAt).TotalHours;
                if (hours > 0.02)
                {
                    _learnedPerHour = 10 / hours;
                    _learnedRising = rising;
                }
            }

            _previousChangeAt = at;
            _anchorAt = at;
            _anchorRising = rising;
            _anchorPercent = rising ? stepLowerBound : Math.Min(100, stepLowerBound + 9);
        }

        _step = stepLowerBound;

        if (stepLowerBound >= 100)
        {
            return new BatteryEstimate(100, true);
        }

        if (_anchorAt is not { } anchorAt)
        {
            return new BatteryEstimate(stepLowerBound + 5, false);
        }

        var risingNow = charging ?? _anchorRising;
        var rate = _learnedRising == risingNow && _learnedPerHour is { } learned
            ? learned
            : risingNow ? DefaultChargePerHour : DefaultDrainPerHour;
        var elapsed = Math.Max(0, (at - anchorAt).TotalHours);

        // If the direction changed since the anchor (e.g. unplugged right after crossing up), the anchor still
        // bounds the value; extrapolate in the current direction from it.
        var estimate = _anchorPercent + (risingNow ? 1 : -1) * rate * elapsed;
        var clamped = (int)Math.Round(Math.Clamp(estimate, stepLowerBound, stepLowerBound + 9));
        return new BatteryEstimate(clamped, true);
    }
}
