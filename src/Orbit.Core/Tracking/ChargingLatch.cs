using Orbit.Core.Parsing;

namespace Orbit.Core.Tracking;

/// <summary>
/// Smooths the charging flag. On the AirPods Max (USB-C) the charging bit is never set while unplugged, but
/// can drop out for ~10 s while charging (docs/protocol-findings.md F6). So "charging" is held for
/// <see cref="Hold"/> after the bit was last seen; "not charging" is reported only after that.
/// </summary>
public sealed class ChargingLatch(TimeSpan? hold = null)
{
    private DateTimeOffset? _lastCharging;

    public TimeSpan Hold { get; } = hold ?? TimeSpan.FromSeconds(30);

    public Reading<bool> Update(Reading<bool> raw, DateTimeOffset at)
    {
        if (!raw.HasValue)
        {
            return raw;
        }

        if (raw.Value)
        {
            _lastCharging = at;
            return raw;
        }

        return _lastCharging is { } last && at - last < Hold
            ? raw with { Value = true }
            : raw;
    }

    public void Reset() => _lastCharging = null;
}
