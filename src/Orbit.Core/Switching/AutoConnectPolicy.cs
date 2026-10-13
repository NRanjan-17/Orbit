namespace Orbit.Core.Switching;

/// <summary>
/// Decides when to auto-connect: Windows playback has just started, the headphones are nearby (heard in BLE
/// advertisements above the RSSI threshold) and they are not connected to this PC. Fires once per playback
/// start, and not again within <see cref="Cooldown"/>, so a failed attempt (e.g. the Mac holds the
/// headphones) is not retried every time a track changes.
/// </summary>
public sealed class AutoConnectPolicy(TimeSpan? cooldown = null)
{
    private bool _wasPlaying;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

    public TimeSpan Cooldown { get; } = cooldown ?? TimeSpan.FromMinutes(2);

    /// <summary>Call on every playback/nearby/connection update. Returns true when a connect should be attempted now.</summary>
    public bool ShouldConnect(bool enabled, bool playing, bool nearby, bool connected, DateTimeOffset now)
    {
        var started = playing && !_wasPlaying;
        _wasPlaying = playing;

        if (!enabled || !started || !nearby || connected || now - _lastAttempt < Cooldown)
        {
            return false;
        }

        _lastAttempt = now;
        return true;
    }
}
