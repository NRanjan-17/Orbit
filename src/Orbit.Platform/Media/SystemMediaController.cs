using Orbit.Core.Media;
using Windows.Media.Control;

namespace Orbit.Platform.Media;

/// <summary>
/// <see cref="IMediaController"/> over GlobalSystemMediaTransportControlsSessionManager (GSMTC): the same
/// sessions shown in the Windows media flyout. Apps without a media session (some players, older desktop apps)
/// cannot be paused this way.
/// </summary>
public sealed class SystemMediaController : IMediaController
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public async Task<IReadOnlyList<IMediaSession>> GetSessionsAsync()
    {
        var manager = _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return manager.GetSessions().Select(s => (IMediaSession)new Session(manager, s)).ToList();
    }

    /// <summary>Wraps the session object itself, so it can be resumed later even if other sessions share its app ID.</summary>
    private sealed class Session(GlobalSystemMediaTransportControlsSessionManager manager, GlobalSystemMediaTransportControlsSession session) : IMediaSession
    {
        public string AppId => session.SourceAppUserModelId;

        public bool IsPlaying => session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

        public bool IsAlive => manager.GetSessions().Contains(session);

        public async Task<bool> PauseAsync() => await session.TryPauseAsync();

        public async Task<bool> PlayAsync() => await session.TryPlayAsync();
    }
}
