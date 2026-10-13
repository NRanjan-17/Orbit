namespace Orbit.Core.Media;

/// <summary>Snapshot of the system's current media session.</summary>
/// <param name="SessionId">Stable ID of the app that owns the session (e.g. its AppUserModelID).</param>
public sealed record MediaState(string SessionId, bool IsPlaying);

/// <summary>Windows media-session control (implemented in Orbit.Platform with GSMTC).</summary>
public interface IMediaController
{
    /// <summary>The current session, or null when nothing has a media session.</summary>
    Task<MediaState?> GetCurrentAsync();

    /// <summary>Pauses the given session. Returns false if the app rejected it.</summary>
    Task<bool> PauseAsync(string sessionId);

    /// <summary>Resumes the given session. Returns false if it no longer exists or rejected it.</summary>
    Task<bool> PlayAsync(string sessionId);
}
