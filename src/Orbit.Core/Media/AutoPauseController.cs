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
    private string? _pausedSessionId;

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

    /// <summary>True while Orbit is holding a session it paused.</summary>
    public bool HoldingPause => _pausedSessionId is not null;

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
        _pausedSessionId = null;
    }

    private async Task<AutoPauseAction> OnTakenOffAsync()
    {
        if (!PauseEnabled)
        {
            return AutoPauseAction.None;
        }

        var current = await _media.GetCurrentAsync();
        if (current is not { IsPlaying: true })
        {
            return AutoPauseAction.None;
        }

        if (await _media.PauseAsync(current.SessionId))
        {
            _pausedSessionId = current.SessionId;
            return AutoPauseAction.Paused;
        }

        return AutoPauseAction.None;
    }

    private async Task<AutoPauseAction> OnPutOnAsync()
    {
        var sessionId = _pausedSessionId;
        _pausedSessionId = null;
        if (!ResumeEnabled || sessionId is null)
        {
            return AutoPauseAction.None;
        }

        // If the user already resumed it, or started something else, leave it alone.
        var current = await _media.GetCurrentAsync();
        if (current is { IsPlaying: true })
        {
            return AutoPauseAction.None;
        }

        return await _media.PlayAsync(sessionId) ? AutoPauseAction.Resumed : AutoPauseAction.None;
    }
}
