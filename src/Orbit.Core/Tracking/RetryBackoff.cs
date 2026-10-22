namespace Orbit.Core.Tracking;

/// <summary>
/// When to try again after a failure: first retry after <see cref="Initial"/>, doubling up to <see cref="Maximum"/>.
/// Used to restart Bluetooth scanning after Windows stopped it (radio turned off, adapter unplugged).
/// </summary>
public sealed class RetryBackoff(TimeSpan initial, TimeSpan maximum)
{
    private DateTimeOffset? _nextAttempt;
    private TimeSpan _delay = initial;

    public TimeSpan Initial { get; } = initial;

    public TimeSpan Maximum { get; } = maximum;

    /// <summary>True while a failure is waiting to be retried.</summary>
    public bool Pending => _nextAttempt is not null;

    /// <summary>Records a failure (or a stop) at <paramref name="at"/> and schedules the next attempt.</summary>
    public void Failed(DateTimeOffset at)
    {
        _nextAttempt = at + _delay;
        _delay = TimeSpan.FromTicks(Math.Min(_delay.Ticks * 2, Maximum.Ticks));
    }

    /// <summary>True when an attempt is due.</summary>
    public bool Due(DateTimeOffset now) => _nextAttempt is { } next && now >= next;

    /// <summary>Back to normal after a successful attempt.</summary>
    public void Succeeded()
    {
        _nextAttempt = null;
        _delay = Initial;
    }
}
