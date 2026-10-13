using Orbit.Core.Analysis;
using Orbit.Core.Capture;

namespace Orbit.Tests.Analysis;

/// <summary>
/// Tests the analyser's diffing logic with synthetic frames. These bytes are NOT a claim
/// about any real device layout; they only exercise grouping and bit detection.
/// </summary>
public class CaptureAnalyzerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static AdvertisementRecord Frame(string tag, short rssi, byte[] payload, int i) =>
        new(RecordKind.Advertisement, T0.AddMilliseconds(i * 100), (ulong)(1000 + (i % 3)), rssi, "ConnectableUndirected", 0x004C, payload, tag, "");

    [Fact]
    public void FindsBitThatSeparatesTwoTags()
    {
        var random = new Random(42);
        var records = new List<AdvertisementRecord>();
        for (var i = 0; i < 60; i++)
        {
            var onHead = i < 30;
            var payload = new byte[] { 0x07, 0x05, 0xAA, 0xBB, 0xCC, (byte)(onHead ? 0x22 : 0x20), 0x55, (byte)random.Next(256) };
            records.Add(Frame(onHead ? "on" : "off", -50, payload, i));
        }

        var report = CaptureAnalyzer.Analyze(records);

        var diff = Assert.Single(report.BitDifferences);
        Assert.Equal(5, diff.Offset);
        Assert.Equal(1, diff.Bit);
        Assert.True(report.Offsets[7].LikelyRandom);
        Assert.False(report.Offsets[6].LikelyRandom);
    }

    [Fact]
    public void SelectsStrongestDeviceGroup()
    {
        var records = new List<AdvertisementRecord>();
        for (var i = 0; i < 10; i++)
        {
            records.Add(Frame("x", -80, [0x07, 0x05, 0x01, 0x01, 0x01, 0x00], i));
            records.Add(Frame("x", -45, [0x07, 0x05, 0x01, 0x02, 0x02, 0x00], i));
        }

        var report = CaptureAnalyzer.Analyze(records);

        Assert.Equal(2, report.Groups.Count);
        Assert.Equal("0705010202", report.SelectedPrefix);
        Assert.Equal(10, report.SelectedCount);
        Assert.Contains("0705010202", CaptureAnalyzer.FormatReport(report));
    }

    [Fact]
    public void IgnoresOtherAppleMessageTypesAndMarkers()
    {
        var records = new List<AdvertisementRecord>
        {
            AdvertisementRecord.Marker(T0, "on"),
            Frame("on", -40, [0x10, 0x05, 0x01], 1),
            Frame("on", -40, [0x07, 0x05, 0x01, 0x02, 0x03], 2),
        };

        var report = CaptureAnalyzer.Analyze(records);

        Assert.Equal(1, report.FrameCount);
        Assert.Single(report.Markers);
    }
}
