using Orbit.Core.Capture;

namespace Orbit.Tests.Capture;

public class CaptureCsvTests
{
    [Fact]
    public void RoundTripsAdvertisementAndMarkerRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orbit-test-{Guid.NewGuid():N}.csv");
        try
        {
            var t0 = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
            using (var session = new CaptureSession(path))
            {
                session.Tag("on-head", t0, "note, with \"quotes\"");
                Assert.True(session.Record(t0.AddSeconds(1), 0xAABBCCDDEEFF, -48, "ConnectableUndirected", 0x004C, [0x07, 0x19, 0x01, 0x02]));
                Assert.False(session.Record(t0.AddSeconds(2), 1, -40, "x", 0x0006, [0x01]));
            }

            var rows = CaptureCsv.ReadFile(path);
            Assert.Equal(2, rows.Count);

            Assert.Equal(RecordKind.Marker, rows[0].Kind);
            Assert.Equal("on-head", rows[0].Tag);
            Assert.Equal("note, with \"quotes\"", rows[0].Note);

            var adv = rows[1];
            Assert.Equal(RecordKind.Advertisement, adv.Kind);
            Assert.Equal(0xAABBCCDDEEFFUL, adv.Address);
            Assert.Equal("AA:BB:CC:DD:EE:FF", adv.AddressText);
            Assert.Equal(-48, adv.Rssi);
            Assert.Equal(0x004C, adv.CompanyId);
            Assert.Equal((byte)0x07, adv.AppleMessageType);
            Assert.Equal("07190102", adv.PayloadHex);
            Assert.Equal("on-head", adv.Tag);
            Assert.Equal(t0.AddSeconds(1), adv.TimestampUtc);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsFilesWithoutHeader()
    {
        Assert.Throws<FormatException>(() => CaptureCsv.Read(new StringReader("a,b,c\n")).ToList());
    }
}
