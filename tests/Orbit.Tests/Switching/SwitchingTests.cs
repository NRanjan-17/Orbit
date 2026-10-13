using Orbit.Core.Switching;

namespace Orbit.Tests.Switching;

public class SwitchCoordinatorTests
{
    private sealed class FakeLink : IAudioConnector, IConnectionState
    {
        public string Name => "fake";
        public bool IsConnected { get; set; }
        public int ConnectAfterRequests { get; set; } = 1; // int.MaxValue = never (e.g. Mac holds the headphones)
        public int ConnectRequests { get; private set; }
        public int DisconnectRequests { get; private set; }

        public Task<bool> RequestConnectAsync(CancellationToken ct)
        {
            if (++ConnectRequests >= ConnectAfterRequests)
            {
                IsConnected = true;
            }

            return Task.FromResult(true);
        }

        public Task<bool> RequestDisconnectAsync(CancellationToken ct)
        {
            DisconnectRequests++;
            IsConnected = false;
            return Task.FromResult(true);
        }
    }

    private static SwitchCoordinator Create(FakeLink link) => new(link, link)
    {
        Backoff = [TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(10)],
        VerifyTimeout = TimeSpan.FromMilliseconds(30),
        VerifyPollInterval = TimeSpan.FromMilliseconds(5),
        FailureCooldown = TimeSpan.FromSeconds(30),
    };

    [Fact]
    public async Task ConnectsOnFirstAttempt()
    {
        var link = new FakeLink();
        var r = await Create(link).ConnectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SwitchOutcome.Connected, r.Outcome);
        Assert.Equal(1, r.Attempts);
    }

    [Fact]
    public async Task RetriesWithBackoff()
    {
        var link = new FakeLink { ConnectAfterRequests = 3 };
        var r = await Create(link).ConnectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SwitchOutcome.Connected, r.Outcome);
        Assert.Equal(3, r.Attempts);
    }

    [Fact]
    public async Task FailsWithClearMessageThenCoolsDown()
    {
        var link = new FakeLink { ConnectAfterRequests = int.MaxValue };
        var c = Create(link);

        var r = await c.ConnectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SwitchOutcome.Failed, r.Outcome);
        Assert.Equal(3, r.Attempts); // 1 + 2 backoff retries
        Assert.Contains("in use by another device", r.Message);

        var again = await c.ConnectAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SwitchOutcome.CoolingDown, again.Outcome);
        Assert.Equal(3, link.ConnectRequests); // nothing sent during cooldown
    }

    [Fact]
    public async Task NoOpWhenAlreadyInRequestedState()
    {
        var link = new FakeLink { IsConnected = true };
        var c = Create(link);
        Assert.Equal(SwitchOutcome.AlreadyConnected, (await c.ConnectAsync(TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(SwitchOutcome.Released, (await c.ReleaseAsync(TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(SwitchOutcome.AlreadyReleased, (await c.ReleaseAsync(TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(1, link.DisconnectRequests);
    }
}

public class AutoConnectPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FiresOnPlaybackStartWhenNearbyAndNotConnected()
    {
        var p = new AutoConnectPolicy();
        Assert.False(p.ShouldConnect(true, playing: false, nearby: true, connected: false, T0));
        Assert.True(p.ShouldConnect(true, playing: true, nearby: true, connected: false, T0.AddSeconds(1)));
        Assert.False(p.ShouldConnect(true, playing: true, nearby: true, connected: false, T0.AddSeconds(2))); // still playing: not a new start
    }

    [Theory]
    [InlineData(false, true, false)] // disabled
    [InlineData(true, false, false)] // not nearby
    [InlineData(true, true, true)]   // already connected
    public void DoesNotFireOtherwise(bool enabled, bool nearby, bool connected)
    {
        var p = new AutoConnectPolicy();
        p.ShouldConnect(enabled, false, nearby, connected, T0);
        Assert.False(p.ShouldConnect(enabled, true, nearby, connected, T0.AddSeconds(1)));
    }

    [Fact]
    public void CooldownBetweenAttempts()
    {
        var p = new AutoConnectPolicy(TimeSpan.FromMinutes(2));
        Assert.True(p.ShouldConnect(true, true, true, false, T0));
        p.ShouldConnect(true, false, true, false, T0.AddSeconds(10));
        Assert.False(p.ShouldConnect(true, true, true, false, T0.AddSeconds(20)));   // within cooldown
        p.ShouldConnect(true, false, true, false, T0.AddMinutes(3));
        Assert.True(p.ShouldConnect(true, true, true, false, T0.AddMinutes(3).AddSeconds(1)));
    }
}

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+O", HotkeyModifiers.Control | HotkeyModifiers.Alt, (uint)'O')]
    [InlineData("win + shift + f9", HotkeyModifiers.Win | HotkeyModifiers.Shift, 0x78u)]
    [InlineData("Ctrl+1", HotkeyModifiers.Control, (uint)'1')]
    public void Parses(string text, HotkeyModifiers mods, uint vk)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(mods, g!.Modifiers);
        Assert.Equal(vk, g.VirtualKey);
    }

    [Theory]
    [InlineData("O")]            // no modifier
    [InlineData("Ctrl+Alt")]     // no key
    [InlineData("Ctrl+A+B")]     // two keys
    [InlineData("Ctrl+Space")]   // unsupported key name
    [InlineData("")]
    public void Rejects(string text) => Assert.False(HotkeyGesture.TryParse(text, out _));
}
