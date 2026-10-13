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
    private readonly object _gate = new();
    private readonly HashSet<GlobalSystemMediaTransportControlsSession> _watched = [];
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    /// <summary>Raised when sessions come and go or any session's playback state changes (event-driven, no polling).</summary>
    public event EventHandler? Changed;

    public async Task<IReadOnlyList<IMediaSession>> GetSessionsAsync()
    {
        var manager = await GetManagerAsync();
        return manager.GetSessions().Select(s => (IMediaSession)new Session(manager, s)).ToList();
    }

    /// <summary>Starts listening for playback changes so <see cref="Changed"/> fires immediately.</summary>
    public async Task StartWatchingAsync() => await GetManagerAsync();

    private async Task<GlobalSystemMediaTransportControlsSessionManager> GetManagerAsync()
    {
        if (_manager is not null)
        {
            return _manager;
        }

        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        lock (_gate)
        {
            if (_manager is not null)
            {
                return _manager;
            }

            _manager = manager;
            manager.SessionsChanged += (_, _) =>
            {
                WatchSessions(manager);
                Changed?.Invoke(this, EventArgs.Empty);
            };
            WatchSessions(manager);
        }

        return manager;
    }

    private void WatchSessions(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        lock (_gate)
        {
            foreach (var session in manager.GetSessions())
            {
                if (_watched.Add(session))
                {
                    session.PlaybackInfoChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
                }
            }
        }
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
