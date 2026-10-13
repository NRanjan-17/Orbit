using Orbit.Core;
using Orbit.Core.Media;
using Orbit.Core.Parsing;
using Orbit.Core.Settings;
using Orbit.Core.Tracking;

namespace Orbit.Tests.Tracking;

public class DebouncerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RequiresConsistentReadingsOverTime()
    {
        var d = new Debouncer<bool>(2, TimeSpan.FromSeconds(1.5));
        Assert.False(d.Add(true, T0));
        Assert.False(d.Add(true, T0.AddSeconds(1)));       // 2 readings but only 1 s
        Assert.True(d.Add(true, T0.AddSeconds(2)));        // 3 readings over 2 s
        Assert.True(d.Stable);
    }

    [Fact]
    public void FlappingNeverSettles()
    {
        var d = new Debouncer<bool>(2, TimeSpan.FromSeconds(1.5));
        d.Add(true, T0);
        d.Add(true, T0.AddSeconds(2));
        for (var i = 3; i < 20; i++)
        {
            Assert.False(d.Add(i % 2 == 0, T0.AddSeconds(i * 0.5)));
        }

        Assert.True(d.Stable);
    }

    [Fact]
    public void UnknownReadingsAreIgnored()
    {
        var d = new Debouncer<bool>(2, TimeSpan.FromSeconds(1));
        d.Add(true, T0);
        d.Add(null, T0.AddSeconds(0.5));
        Assert.True(d.Add(true, T0.AddSeconds(1)));
    }
}

public class AutoPauseControllerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class FakeSession(string appId, string name, List<string> calls) : IMediaSession
    {
        public string AppId => appId;
        public bool IsPlaying { get; set; }
        public bool IsAlive { get; set; } = true;

        public Task<bool> PauseAsync() { calls.Add("pause " + name); IsPlaying = false; return Task.FromResult(true); }

        public Task<bool> PlayAsync() { calls.Add("play " + name); IsPlaying = true; return Task.FromResult(true); }
    }

    private sealed class FakeMedia : IMediaController
    {
        public FakeMedia() => Sessions.Add(new FakeSession("Spotify", "Spotify", Calls));

        public List<string> Calls { get; } = [];
        public List<FakeSession> Sessions { get; } = [];

        /// <summary>Playing state of the first session (the simple single-player case).</summary>
        public bool Playing { get => Sessions[0].IsPlaying; set => Sessions[0].IsPlaying = value; }

        public Task<IReadOnlyList<IMediaSession>> GetSessionsAsync() => Task.FromResult<IReadOnlyList<IMediaSession>>(Sessions);

        public event EventHandler? Changed { add { } remove { } }
    }

    private static Reading<bool> Worn(bool v, Confidence c = Confidence.VerifiedOnDevice) => new(v, c, "test");

    private static async Task<AutoPauseAction> Feed(AutoPauseController c, bool worn, int fromSecond, Confidence conf = Confidence.VerifiedOnDevice)
    {
        var last = AutoPauseAction.None;
        for (var s = fromSecond; s < fromSecond + 3; s++)
        {
            var a = await c.OnReadingAsync(Worn(worn, conf), T0.AddSeconds(s));
            if (a != AutoPauseAction.None)
            {
                last = a;
            }
        }

        return last;
    }

    [Fact]
    public async Task PausesOnRemoveAndResumesOnPutOn()
    {
        var media = new FakeMedia { Playing = true };
        var c = new AutoPauseController(media);

        Assert.Equal(AutoPauseAction.None, await Feed(c, true, 0));     // initial state never acts
        Assert.Equal(AutoPauseAction.Paused, await Feed(c, false, 10));
        Assert.Equal(AutoPauseAction.Resumed, await Feed(c, true, 20));
        Assert.Equal(["pause Spotify", "play Spotify"], media.Calls);
    }

    [Fact]
    public async Task DoesNotResumeWhatItDidNotPause()
    {
        var media = new FakeMedia { Playing = false };
        var c = new AutoPauseController(media);
        await Feed(c, true, 0);
        Assert.Equal(AutoPauseAction.None, await Feed(c, false, 10)); // nothing playing
        Assert.Equal(AutoPauseAction.None, await Feed(c, true, 20));
        Assert.Empty(media.Calls);
    }

    [Fact]
    public async Task DoesNotResumeIfUserAlreadyResumed()
    {
        var media = new FakeMedia { Playing = true };
        var c = new AutoPauseController(media);
        await Feed(c, true, 0);
        await Feed(c, false, 10);
        media.Playing = true; // user pressed play while headphones were off
        Assert.Equal(AutoPauseAction.None, await Feed(c, true, 20));
        Assert.Equal(["pause Spotify"], media.Calls);
    }

    [Fact]
    public async Task ResumesExactlyTheBackgroundTabItPaused()
    {
        // Owner's report: music in a background Chrome tab paused but never resumed. Two tabs share the app ID
        // "Chrome"; the first (idle) tab is what an app-ID lookup would have found.
        var media = new FakeMedia();
        media.Sessions.Clear();
        var idleTab = new FakeSession("Chrome", "tab-1", media.Calls);
        var musicTab = new FakeSession("Chrome", "tab-2", media.Calls) { IsPlaying = true };
        media.Sessions.AddRange([idleTab, musicTab]);

        var c = new AutoPauseController(media);
        await Feed(c, true, 0);
        Assert.Equal(AutoPauseAction.Paused, await Feed(c, false, 10));
        Assert.Equal(AutoPauseAction.Resumed, await Feed(c, true, 20));

        Assert.Equal(["pause tab-2", "play tab-2"], media.Calls);
        Assert.True(musicTab.IsPlaying);
        Assert.False(idleTab.IsPlaying);
    }

    [Fact]
    public async Task DoesNotResumeAClosedSession()
    {
        var media = new FakeMedia { Playing = true };
        var c = new AutoPauseController(media);
        await Feed(c, true, 0);
        await Feed(c, false, 10);
        media.Sessions[0].IsAlive = false; // player closed while the headphones were off
        Assert.Equal(AutoPauseAction.None, await Feed(c, true, 20));
    }

    [Fact]
    public async Task IgnoresUnverifiedOnHeadReadings()
    {
        var media = new FakeMedia { Playing = true };
        var c = new AutoPauseController(media);
        await Feed(c, true, 0, Confidence.Corroborated);
        Assert.Equal(AutoPauseAction.None, await Feed(c, false, 10, Confidence.Corroborated));
        Assert.Empty(media.Calls);
    }

    [Fact]
    public async Task RespectsDisabledPause()
    {
        var media = new FakeMedia { Playing = true };
        var c = new AutoPauseController(media) { PauseEnabled = false };
        await Feed(c, true, 0);
        Assert.Equal(AutoPauseAction.None, await Feed(c, false, 10));
    }
}

public class DeviceTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ModelProfile Bound = BoundDeviceProfile.Create("My headphones", 0x004C, 0x201F, 0x240418)!;

    private static byte[] Frame(byte m3, byte m4, byte status = 0x01, byte color = 0x12)
    {
        var p = new byte[27];
        p[0] = 0x07; p[1] = 0x19; p[2] = 0x01; p[3] = m3; p[4] = m4; p[5] = status; p[6] = 0x03; p[7] = 0x80; p[9] = color;
        return p;
    }

    [Fact]
    public void FiltersByRssiThreshold()
    {
        var t = new DeviceTracker(Bound, rssiThreshold: -70);
        Assert.Null(t.Add(Frame(0x1F, 0x20), -85, T0));
        Assert.NotNull(t.Add(Frame(0x1F, 0x20), -50, T0));
    }

    [Fact]
    public void BoundDeviceIgnoresOtherModels()
    {
        var t = new DeviceTracker(Bound);
        Assert.Null(t.Add(Frame(0x14, 0x20), -40, T0)); // somebody's AirPods Pro 2, very close
        Assert.Null(t.Current);
    }

    [Fact]
    public void WithoutBindingPicksNearestAndIsSticky()
    {
        var t = new DeviceTracker(null);
        t.Add(Frame(0x14, 0x20), -60, T0);
        t.Add(Frame(0x0E, 0x20), -55, T0);   // 5 dB stronger: within hysteresis, keep the first
        Assert.Equal(0x1420, t.Current!.Status.ModelId);
        for (var i = 1; i < 6; i++)
        {
            t.Add(Frame(0x0E, 0x20), -40, T0.AddSeconds(i));
        }

        Assert.Equal(0x0E20, t.Current!.Status.ModelId); // clearly closer now
    }

    [Fact]
    public void LosesDeviceWhenItStopsAdvertising()
    {
        // Observed: the Max stopped advertising while charging / idle (protocol-findings F5).
        var t = new DeviceTracker(Bound, staleAfter: TimeSpan.FromSeconds(20));
        t.Add(Frame(0x1F, 0x20), -50, T0);
        Assert.False(t.Prune(T0.AddSeconds(10)));
        Assert.True(t.Prune(T0.AddSeconds(30)));
        Assert.Null(t.Current);
    }

    [Fact]
    public void AddressIsNotPartOfIdentity()
    {
        // Same payload from a "different address" is the same device: the API doesn't even take an address.
        var t = new DeviceTracker(Bound);
        t.Add(Frame(0x1F, 0x20), -50, T0);
        t.Add(Frame(0x1F, 0x20, status: 0x2B), -50, T0.AddSeconds(1));
        Assert.True(t.Current!.Status.Worn.Value);
    }
}

public class LowBatteryMonitorTests
{
    private static Reading<int> Pct(int v) => new(v, Confidence.VerifiedOnDevice, "t");
    private static readonly Reading<bool> NotCharging = Reading<bool>.Unknown();

    [Fact]
    public void WarnsOnceThenRearmsAfterRecovery()
    {
        var m = new LowBatteryMonitor(20);
        Assert.Null(m.Check(Pct(30), NotCharging));
        Assert.Equal(20, m.Check(Pct(20), NotCharging));
        Assert.Null(m.Check(Pct(20), NotCharging));
        Assert.Null(m.Check(Pct(10), NotCharging));
        Assert.Null(m.Check(Pct(30), NotCharging));
        Assert.Equal(20, m.Check(Pct(20), NotCharging));
    }

    [Fact]
    public void IgnoresUnverifiedBattery() =>
        Assert.Null(new LowBatteryMonitor(20).Check(new Reading<int>(10, Confidence.SingleSource, "t"), NotCharging));
}

public class SettingsStoreTests
{
    [Fact]
    public void RoundTripsAndSurvivesCorruptFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orbit-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            Assert.Equal(-70, store.Load().RssiThreshold);

            store.Save(new OrbitSettings { RssiThreshold = -60, AutoPause = false, BoundDeviceId = "x" });
            var loaded = store.Load();
            Assert.Equal(-60, loaded.RssiThreshold);
            Assert.False(loaded.AutoPause);
            Assert.Equal("x", loaded.BoundDeviceId);

            File.WriteAllText(path, "{ not json");
            Assert.Equal(-70, store.Load().RssiThreshold);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
