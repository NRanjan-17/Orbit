using System.Runtime.InteropServices.WindowsRuntime;
using Orbit.Core.Audio;
using Windows.Devices.Enumeration;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace Orbit.Platform.Audio;

/// <summary>
/// Plays Orbit's chime on the headphones' own stereo output (matched by ContainerId), not on whatever the
/// default device is, so it is never played through the laptop speakers.
/// </summary>
public static class ChimePlayer
{
    private const string ContainerIdKey = "System.Devices.ContainerId";
    private static readonly byte[] Wav = ChimeGenerator.CreateWav();
    private static MediaPlayer? _player;

    /// <returns>False if the headphones have no active audio output on this PC.</returns>
    public static async Task<bool> PlayAsync(Guid containerId)
    {
        var outputs = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector(), [ContainerIdKey]);
        var mine = outputs.Where(o => o.Properties.TryGetValue(ContainerIdKey, out var c) && c is Guid g && g == containerId).ToList();

        // Prefer the stereo A2DP "Headphones" output over the Hands-Free one (which would start a call-quality link).
        var target = mine.FirstOrDefault(o => o.Name.StartsWith("Headphones", StringComparison.OrdinalIgnoreCase)) ?? mine.FirstOrDefault();
        if (target is null)
        {
            return false;
        }

        var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(Wav.AsBuffer());
        stream.Seek(0);

        _player?.Dispose();
        _player = new MediaPlayer
        {
            AudioDevice = target,
            AudioCategory = MediaPlayerAudioCategory.Alerts,
            Source = MediaSource.CreateFromStream(stream, "audio/wav"),
        };
        _player.Play();
        return true;
    }
}
