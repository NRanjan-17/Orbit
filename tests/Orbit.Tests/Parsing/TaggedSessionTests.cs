using Orbit.Core;
using Orbit.Core.Capture;
using Orbit.Core.Parsing;

namespace Orbit.Tests.Parsing;

/// <summary>
/// Ground truth from the owner's tagged capture session (2026-10-13, AirPods Max USB-C).
/// Fixture is anonymised: Max frames only, address replaced, encrypted tail zeroed; markers kept.
/// See docs/protocol-findings.md F4.
/// </summary>
public class TaggedSessionTests
{
    private static readonly ModelProfile Bound = BoundDeviceProfile.Create("My headphones", 0x004C, 0x201F, 0x240418)!;

    private static readonly IReadOnlyList<AdvertisementRecord> Session =
        CaptureCsv.ReadFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tagged-session-max-usbc.csv"));

    /// <summary>
    /// Frames inside each on-head/off-head window. The Max reports the change ~2-4 s before the user
    /// clicks the tag, so we skip 1 s after a tag and the last 3 s before the next marker.
    /// </summary>
    private static IEnumerable<(string Tag, AdvertisementRecord Frame)> Windows(params string[] tags)
    {
        var markers = Session.Where(r => r.Kind == RecordKind.Marker).ToList();
        for (var i = 0; i < markers.Count - 1; i++)
        {
            if (!tags.Contains(markers[i].Tag))
            {
                continue;
            }

            var from = markers[i].TimestampUtc.AddSeconds(1);
            var to = markers[i + 1].TimestampUtc.AddSeconds(-3);
            foreach (var f in Session.Where(r => r.Kind == RecordKind.Advertisement && r.TimestampUtc >= from && r.TimestampUtc <= to))
            {
                yield return (markers[i].Tag, f);
            }
        }
    }

    [Fact]
    public void OnHeadMatchesEveryTaggedWindow()
    {
        var samples = Windows(CaptureTags.OnHead, CaptureTags.OffHead).ToList();
        // Tag windows were short (5-15 s), so only a handful of frames fall inside the settle margins.
        Assert.True(samples.Count(s => s.Tag == CaptureTags.OnHead) >= 2, $"on-head frames: {samples.Count(s => s.Tag == CaptureTags.OnHead)}");
        Assert.True(samples.Count(s => s.Tag == CaptureTags.OffHead) >= 2, $"off-head frames: {samples.Count(s => s.Tag == CaptureTags.OffHead)}");

        foreach (var (tag, frame) in samples)
        {
            var worn = ProximityPairingParser.Parse(frame.Payload, ModelRegistry.WithBound(Bound))!.Worn;
            Assert.Equal(Confidence.VerifiedOnDevice, worn.Confidence);
            Assert.Equal(tag == CaptureTags.OnHead, worn.Value);
        }
    }

    private sealed class PlayingMedia : Orbit.Core.Media.IMediaController, Orbit.Core.Media.IMediaSession
    {
        public string AppId => "player";
        public bool IsPlaying { get; private set; } = true;
        public bool IsAlive => true;

        public Task<IReadOnlyList<Orbit.Core.Media.IMediaSession>> GetSessionsAsync() =>
            Task.FromResult<IReadOnlyList<Orbit.Core.Media.IMediaSession>>([this]);

        public Task<bool> PauseAsync() { IsPlaying = false; return Task.FromResult(true); }

        public Task<bool> PlayAsync() { IsPlaying = true; return Task.FromResult(true); }

        public event EventHandler? Changed { add { } remove { } }
    }

    [Fact]
    public async Task FastAutoPauseOnRealSessionActsOncePerTransition()
    {
        // Replays the whole capture with music playing: off -> on -> off -> on -> off (Smart Case, unknown) -> on.
        var controller = new Orbit.Core.Media.AutoPauseController(new PlayingMedia(), requiredReadings: 1, minimumDuration: TimeSpan.Zero);
        var actions = new List<Orbit.Core.Media.AutoPauseAction>();
        foreach (var frame in Session.Where(r => r.Kind == RecordKind.Advertisement))
        {
            var status = ProximityPairingParser.Parse(frame.Payload, ModelRegistry.WithBound(Bound))!;
            var a = await controller.OnReadingAsync(status.Worn, frame.TimestampUtc);
            if (a != Orbit.Core.Media.AutoPauseAction.None)
            {
                actions.Add(a);
            }
        }

        // First "on" resumes nothing (Orbit had not paused); then every off pauses and every on resumes.
        Assert.Equal(
            [Orbit.Core.Media.AutoPauseAction.Paused, Orbit.Core.Media.AutoPauseAction.Resumed,
             Orbit.Core.Media.AutoPauseAction.Paused, Orbit.Core.Media.AutoPauseAction.Resumed],
            actions);
    }

    [Fact]
    public void InCaseWhileChargingIsNotMistakenForOnHead()
    {
        // 0x3F (in Smart Case + charging) contains all bits of 0x2B (on head): must be unknown, not "worn".
        var inCase = Session.Where(r => r.Kind == RecordKind.Advertisement && r.Payload[5] == 0x3F).ToList();
        Assert.NotEmpty(inCase);
        foreach (var frame in inCase)
        {
            Assert.False(ProximityPairingParser.Parse(frame.Payload, ModelRegistry.WithBound(Bound))!.Worn.HasValue);
        }
    }

    [Fact]
    public void NoiseModeDoesNotChangeUnencryptedBytes()
    {
        // ANC / transparency / off tags all occurred while on head; d[0..9] must be identical across them
        // (d[10] is excluded: it varies independently, meaning unknown).
        var heads = Windows(CaptureTags.AncOn, CaptureTags.Transparency, CaptureTags.NoiseOff)
            .Select(s => Hex.Format(s.Frame.Payload.AsSpan(0, 10)))
            .Distinct()
            .ToList();
        Assert.Single(heads);
    }
}
