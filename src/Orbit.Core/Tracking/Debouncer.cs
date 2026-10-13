namespace Orbit.Core.Tracking;

/// <summary>
/// Accepts a new value only after it has been seen in <see cref="RequiredReadings"/> consecutive readings
/// spanning at least <see cref="MinimumDuration"/>. Unknown readings (null) are ignored: they neither confirm
/// nor reset a pending change. Used to stop on/off-head flapping from triggering pause/resume.
/// </summary>
public sealed class Debouncer<T>
    where T : struct, IEquatable<T>
{
    private T? _candidate;
    private int _candidateCount;
    private DateTimeOffset _candidateSince;

    public Debouncer(int requiredReadings, TimeSpan minimumDuration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(requiredReadings, 1);
        RequiredReadings = requiredReadings;
        MinimumDuration = minimumDuration;
    }

    public int RequiredReadings { get; }

    public TimeSpan MinimumDuration { get; }

    /// <summary>The last accepted value, or null before the first one is accepted.</summary>
    public T? Stable { get; private set; }

    /// <summary>Feeds a reading. Returns true when <see cref="Stable"/> changed.</summary>
    public bool Add(T? reading, DateTimeOffset at)
    {
        if (reading is not { } value)
        {
            return false;
        }

        if (Stable is { } stable && stable.Equals(value))
        {
            _candidate = null;
            _candidateCount = 0;
            return false;
        }

        if (_candidate is { } c && c.Equals(value))
        {
            _candidateCount++;
        }
        else
        {
            _candidate = value;
            _candidateCount = 1;
            _candidateSince = at;
        }

        if (_candidateCount >= RequiredReadings && at - _candidateSince >= MinimumDuration)
        {
            Stable = value;
            _candidate = null;
            _candidateCount = 0;
            return true;
        }

        return false;
    }

    public void Reset()
    {
        Stable = null;
        _candidate = null;
        _candidateCount = 0;
    }
}
