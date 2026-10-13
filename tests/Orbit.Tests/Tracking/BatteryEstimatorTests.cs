using Orbit.Core.Tracking;

namespace Orbit.Tests.Tracking;

public class BatteryEstimatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BeforeAnyStepChangeReturnsMidpointNotAnchored()
    {
        var e = new BatteryEstimator();
        Assert.Equal(new BatteryEstimate(65, false), e.Update(60, false, T0));
    }

    [Fact]
    public void StepDownAnchorsAtUpperEdgeAndCountsDown()
    {
        var e = new BatteryEstimator();
        e.Update(70, false, T0);
        Assert.Equal(new BatteryEstimate(69, true), e.Update(60, false, T0.AddMinutes(1)));    // just crossed: 69 %
        Assert.Equal(64, e.Update(60, false, T0.AddMinutes(1).AddHours(1)).Percent);          // default 5 %/h
    }

    [Fact]
    public void LearnsRateFromConsecutiveSteps()
    {
        var e = new BatteryEstimator();
        e.Update(80, false, T0);
        e.Update(70, false, T0.AddHours(1));       // first crossing (anchor)
        e.Update(60, false, T0.AddHours(3));       // 10 % in 2 h → 5 %/h learned
        Assert.Equal(67, e.Update(60, false, T0.AddHours(3.4)).Percent); // 69 - 0.4 h * 5 = 67
    }

    [Fact]
    public void NeverLeavesTheAdvertisedStep()
    {
        var e = new BatteryEstimator();
        e.Update(70, false, T0);
        e.Update(60, false, T0);
        Assert.Equal(60, e.Update(60, false, T0.AddHours(10)).Percent); // would be 19 %, clamped to the step
    }

    [Fact]
    public void ChargingStepUpAnchorsAtLowerEdgeAndCountsUp()
    {
        var e = new BatteryEstimator();
        e.Update(50, true, T0);
        Assert.Equal(new BatteryEstimate(60, true), e.Update(60, true, T0));
        Assert.Equal(66, e.Update(60, true, T0.AddMinutes(9)).Percent);   // default 40 %/h → +6 in 9 min
    }

    [Fact]
    public void FullIsFull() => Assert.Equal(100, new BatteryEstimator().Update(100, true, T0).Percent);
}
