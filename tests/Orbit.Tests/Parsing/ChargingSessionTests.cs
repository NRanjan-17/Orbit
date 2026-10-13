using Orbit.Core.Capture;
using Orbit.Core.Parsing;
using Orbit.Core.Tracking;

namespace Orbit.Tests.Parsing;

/// <summary>
/// Owner's charging capture (2026-10-13 23:43, Max plugged in by USB-C and awake; Mac showed 65%).
/// Anonymised: address replaced, encrypted tail zeroed. See docs/protocol-findings.md F6.
/// </summary>
public class ChargingSessionTests
{
    private static readonly ModelProfile Bound = BoundDeviceProfile.Create("My headphones", 0x004C, 0x201F, 0x240418)!;

    private static readonly IReadOnlyList<AdvertisementRecord> Frames =
        CaptureCsv.ReadFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "charging-session-max-usbc.csv"))
            .Where(r => r.Kind == RecordKind.Advertisement)
            .ToList();

    [Fact]
    public void BatteryMatchesSecondMacReading()
    {
        foreach (var f in Frames)
        {
            var battery = ProximityPairingParser.Parse(f.Payload, ModelRegistry.WithBound(Bound))!.Battery;
            Assert.Equal(60, battery.Value); // Mac: 65 % -> "60–69%"
        }
    }

    [Fact]
    public void RawChargingBitDropsOutWhilePluggedIn()
    {
        var raw = Frames.Select(f => ProximityPairingParser.Parse(f.Payload, ModelRegistry.WithBound(Bound))!.Charging.Value).ToList();
        Assert.Contains(false, raw);                 // the dropout seen in the capture
        Assert.True(raw.Count(v => v) > raw.Count / 2);
    }

    [Fact]
    public void TrackerReportsChargingThroughoutTheSession()
    {
        var tracker = new DeviceTracker(Bound);
        foreach (var f in Frames)
        {
            var status = tracker.Add(f.Payload, f.Rssi, f.TimestampUtc);
            Assert.NotNull(status);
            Assert.True(status.Charging.Value, $"not charging at {f.TimestampUtc:HH:mm:ss.f}");
        }
    }

    [Fact]
    public void LatchReleasesAfterHold()
    {
        var latch = new ChargingLatch(TimeSpan.FromSeconds(30));
        var t0 = DateTimeOffset.UnixEpoch;
        Reading<bool> R(bool v) => new(v, Confidence.VerifiedOnDevice, "t");

        Assert.False(latch.Update(R(false), t0).Value);           // never charged: not charging
        Assert.True(latch.Update(R(true), t0.AddSeconds(1)).Value);
        Assert.True(latch.Update(R(false), t0.AddSeconds(20)).Value);
        Assert.False(latch.Update(R(false), t0.AddSeconds(40)).Value);
    }
}
