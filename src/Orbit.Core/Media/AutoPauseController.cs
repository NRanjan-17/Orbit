using Orbit.Core.Parsing;
using Orbit.Core.Tracking;

namespace Orbit.Core.Media;

/// <summary>What the auto-pause controller did in response to a reading.</summary>
public enum AutoPauseAction
{
    None,
    Paused,
    Resumed,
}

/// <summary>
/// Pauses playback when the headphones come off and resumes it when they go back on — but only playback
/// that Orbit itself paused. Readings are debounced, and only on-head readings at or above
/// <see cref="MinimumConfidence"/> are used (VerifiedOnDevice by default), so an unverified bit never pauses music.
/// </summary>
public sealed class AutoPauseController
{
    private readonly IMediaController _media;
    private readonly Debouncer<bool> _worn;
    // The exact sessions Orbit paused (references, not app IDs: several Chrome tabs share one app ID, and the
    // session Windows calls "current" is often not the one that was playing, e.g. a background browser tab).
    private List<IMediaSession> _paused = [];

    public AutoPauseController(IMediaController media, int requiredReadings = 2, TimeSpan? minimumDuration = null)
    {
        _media = media;
        _worn = new Debouncer<bool>(requiredReadings, minimumDuration ?? TimeSpan.FromSeconds(1.5));
    }

    public bool PauseEnabled { get; set; } = true;

    public bool ResumeEnabled { get; set; } = true;

    public Confidence MinimumConfidence { get; set; } = Confidence.VerifiedOnDevice;

    /// <summary>Debounced on-head state; null until the first stable reading.</summary>
    public bool? Worn => _worn.Stable;

    /// <summary>True while Orbit is holding sessions it paused.</summary>
    public bool HoldingPause => _paused.Count > 0;

    public async Task<AutoPauseAction> OnReadingAsync(Reading<bool> worn, DateTimeOffset at)
    {
        var usable = worn.IsUsable(MinimumConfidence) ? worn.Value : (bool?)null;
        var previous = _worn.Stable;
        if (!_worn.Add(usable, at) || previous is null)
        {
            // No change, or this is the first stable value (never act on start-up state).
            return AutoPauseAction.None;
        }

        return _worn.Stable == true ? await OnPutOnAsync() : await OnTakenOffAsync();
    }

    /// <summary>Forget any held pause (device lost, settings changed, user took over playback).</summary>
    public void Reset()
    {
        _worn.Reset();
        _paused = [];
    }

    private async Task<AutoPauseAction> OnTakenOffAsync()
    {
        if (!PauseEnabled)
        {
            return AutoPauseAction.None;
        }

        // Pause every session that is actually playing, whichever app or tab is in front.
        var paused = new List<IMediaSession>();
        foreach (var session in (await _media.GetSessionsAsync()).Where(s => s.IsPlaying))
        {
            if (await session.PauseAsync())
            {
                paused.Add(session);
            }
        }

        _paused = paused;
        return paused.Count > 0 ? AutoPauseAction.Paused : AutoPauseAction.None;
    }

    private async Task<AutoPauseAction> OnPutOnAsync()
    {
        var paused = _paused;
        _paused = [];
        if (!ResumeEnabled || paused.Count == 0)
        {
            return AutoPauseAction.None;
        }

        // If the user already resumed something or started other playback meanwhile, leave everything alone.
        if ((await _media.GetSessionsAsync()).Any(s => s.IsPlaying))
        {
            return AutoPauseAction.None;
        }

        var resumed = false;
        foreach (var session in paused.Where(s => s.IsAlive))
        {
            resumed |= await session.PlayAsync();
        }

        return resumed ? AutoPauseAction.Resumed : AutoPauseAction.None;
    }
}
