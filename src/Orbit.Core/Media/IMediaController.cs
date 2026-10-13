namespace Orbit.Core.Media;

/// <summary>
/// One media session (a player tab or app) as Windows sees it. Implementations keep a reference to the actual
/// session, so the same session can be resumed later even when several share an app ID (e.g. Chrome tabs).
/// </summary>
public interface IMediaSession
{
    /// <summary>App that owns the session (e.g. its AppUserModelID). Not unique: Chrome tabs share one.</summary>
    string AppId { get; }

    bool IsPlaying { get; }

    /// <summary>True while the session still exists in Windows.</summary>
    bool IsAlive { get; }

    Task<bool> PauseAsync();

    Task<bool> PlayAsync();
}

/// <summary>Windows media-session control (implemented in Orbit.Platform with GSMTC).</summary>
public interface IMediaController
{
    /// <summary>All current media sessions, not only the one Windows considers "current".</summary>
    Task<IReadOnlyList<IMediaSession>> GetSessionsAsync();

    /// <summary>Raised (on any thread) when a session appears/disappears or its playback state changes.</summary>
    event EventHandler? Changed;
}
