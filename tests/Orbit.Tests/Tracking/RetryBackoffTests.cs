using Orbit.Core.Tracking;

namespace Orbit.Tests.Tracking;

public class RetryBackoffTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NothingIsDueWithoutAFailure()
    {
        var b = new RetryBackoff(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));
        Assert.False(b.Pending);
        Assert.False(b.Due(T0.AddHours(1)));
    }

    [Fact]
    public void RetriesBackOffAndCap()
    {
        var b = new RetryBackoff(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));
        b.Failed(T0);
        Assert.False(b.Due(T0.AddSeconds(1)));
        Assert.True(b.Due(T0.AddSeconds(2)));
        b.Failed(T0.AddSeconds(2)); // next delay 4 s
        Assert.False(b.Due(T0.AddSeconds(5)));
        Assert.True(b.Due(T0.AddSeconds(6)));
        b.Failed(T0.AddSeconds(6)); // 8 s
        b.Failed(T0.AddSeconds(14)); // capped at 10 s
        Assert.False(b.Due(T0.AddSeconds(23)));
        Assert.True(b.Due(T0.AddSeconds(24)));
    }

    [Fact]
    public void SuccessResetsTheSchedule()
    {
        var b = new RetryBackoff(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));
        b.Failed(T0);
        b.Failed(T0.AddSeconds(2));
        b.Succeeded();
        Assert.False(b.Pending);
        b.Failed(T0.AddSeconds(100));
        Assert.True(b.Due(T0.AddSeconds(102)));
    }
}
