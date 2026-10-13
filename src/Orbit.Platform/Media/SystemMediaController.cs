using Orbit.Core.Media;
using Windows.Media.Control;

namespace Orbit.Platform.Media;

/// <summary>
/// <see cref="IMediaController"/> over GlobalSystemMediaTransportControlsSessionManager (GSMTC): the same
/// sessions shown in the Windows media flyout. Apps without a media session (some browsers' players,
/// older desktop apps) cannot be paused this way.
/// </summary>
public sealed class SystemMediaController : IMediaController
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public async Task<MediaState?> GetCurrentAsync()
    {
        var session = (await GetManagerAsync()).GetCurrentSession();
        if (session is null)
        {
            return null;
        }

        var playing = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        return new MediaState(session.SourceAppUserModelId, playing);
    }

    public async Task<bool> PauseAsync(string sessionId) =>
        await Find(await GetManagerAsync(), sessionId) is { } s && await s.TryPauseAsync();

    public async Task<bool> PlayAsync(string sessionId) =>
        await Find(await GetManagerAsync(), sessionId) is { } s && await s.TryPlayAsync();

    private static Task<GlobalSystemMediaTransportControlsSession?> Find(GlobalSystemMediaTransportControlsSessionManager manager, string id) =>
        Task.FromResult(manager.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId == id));

    private async Task<GlobalSystemMediaTransportControlsSessionManager> GetManagerAsync() =>
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
}
