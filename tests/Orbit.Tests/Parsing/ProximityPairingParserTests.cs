using Orbit.Core;
using Orbit.Core.Capture;
using Orbit.Core.Parsing;

namespace Orbit.Tests.Parsing;

public class ProximityPairingParserTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // The bound device as Orbit builds it at runtime from Windows' Device ID values. These are model-level
    // values (Apple vendor 0x004C, product 0x201F, Class of Device "Audio/Video: Headphones"), not identifiers
    // of any particular unit.
    private static readonly ModelProfile Bound = BoundDeviceProfile.Create("My headphones", 0x004C, 0x201F, 0x240418)!;
    private static readonly Func<ushort, ModelProfile?> Lookup = ModelRegistry.WithBound(Bound);

    // ---------------------------------------------------------------------------------------------
    // Real device: probe capture, 2026-10-13 (untagged). Anonymised: address replaced, encrypted tail zeroed.
    // Only proves framing + model ID recognition. Field values are asserted as the parser's *current
    // behaviour* with their confidence levels, NOT as ground truth.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ProbeCapture_AllFramesMatchBoundDevice()
    {
        var frames = CaptureCsv.ReadFile(Fixture("probe-untagged-max-usbc.csv"));
        Assert.Equal(14, frames.Count);

        foreach (var record in frames)
        {
            var status = ProximityPairingParser.Parse(record.Payload, Lookup);
            Assert.NotNull(status);
            Assert.Same(Bound, status.Profile);
            Assert.Equal(DeviceForm.Headphones, status.Form);
            Assert.Equal(16, status.Frame.EncryptedTail.Length);
        }

        // Observed: d[0..9] constant; d[10] changed C4 -> C0 in the last frame (meaning not yet known,
        // see docs/protocol-findings.md). The encrypted tail rotated every few seconds on the real device;
        // it is zeroed in the fixture because it may identify the device.
        Assert.Single(frames.Select(f => Hex.Format(f.Payload.AsSpan(0, 10))).Distinct());
        Assert.Equal(new byte[] { 0xC0, 0xC4 }, frames.Select(f => f.Payload[10]).Distinct().Order().ToArray());
    }

    [Fact]
    public void ProbeCapture_UnverifiedFieldsCarryLowConfidence()
    {
        var status = ProximityPairingParser.Parse(Hex.Parse("0719011F200103800012C400000000000000000000000000000000"), Lookup)!;

        // OpenPods-only rules -> SingleSource. The UI must not present these as fact.
        Assert.Equal(Confidence.SingleSource, status.Battery.Confidence);
        Assert.Equal(Confidence.SingleSource, status.Charging.Confidence);

        // No source documents an on-head bit for Max.
        Assert.False(status.Worn.HasValue);

        // Bytes outside every community table stay unknown rather than being guessed.
        Assert.False(status.Color.HasValue);           // 0x12
        Assert.False(status.ConnectionState.HasValue); // 0xC4
    }

    // ---------------------------------------------------------------------------------------------
    // Earbuds: real sanitised captures published by AirPodsDesktop
    // (Tests/AirPodsDomainTests.cpp L217-224 @ 56b04001, "AirPods 5 with Wireless Charging Case",
    // bytes 11-26 zeroed upstream). Expected values are AirPodsDesktop's own assertions (L241-250).
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("0719013020" + "35AABA32000500000000000000000000000000000000", "left frame")]
    [InlineData("0719013020" + "55AABA32000500000000000000000000000000000000", "right frame")]
    public void AirPodsDesktopCapture_DecodesLikeUpstream(string hex, string _)
    {
        var status = ProximityPairingParser.Parse(Hex.Parse(hex))!;

        Assert.Equal("AirPods 5", status.ModelName);
        Assert.Equal(DeviceForm.Earbuds, status.Form);
        Assert.Equal(100, status.LeftBattery.Value);
        Assert.Equal(100, status.RightBattery.Value);
        Assert.Equal(100, status.CaseBattery.Value);
        Assert.True(status.LeftCharging.Value);
        Assert.True(status.RightCharging.Value);
        Assert.False(status.CaseCharging.Value);
        Assert.False(status.LeftInEar.Value);
        Assert.False(status.RightInEar.Value);
        Assert.Equal(Confidence.Corroborated, status.LeftBattery.Confidence);
    }

    // ---------------------------------------------------------------------------------------------
    // Earbud decoder rules, exercised with frames built exactly like AirPodsDesktop's MakePacket()
    // (AirPodsDomainTests.cpp L104-133): left-broadcast frames put left battery in the low nibble.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Earbuds_BatterySidesFollowPrimaryBit()
    {
        // AirPods Pro 2 (14 20), broadcast from left (0x20), both in ear (0x02|0x08), L=8 R=7 case=5.
        var fromLeft = ProximityPairingParser.Parse(Build(0x14, 0x20, 0x20 | 0x02 | 0x08, (7 << 4) | 8, 0x05))!;
        Assert.Equal(80, fromLeft.LeftBattery.Value);
        Assert.Equal(70, fromLeft.RightBattery.Value);
        Assert.Equal(50, fromLeft.CaseBattery.Value);
        Assert.True(fromLeft.AnyWorn.Value);

        // Same state broadcast from the right pod: nibbles swap (MakePacket L125-127).
        var fromRight = ProximityPairingParser.Parse(Build(0x14, 0x20, 0x02 | 0x08, (8 << 4) | 7, 0x05))!;
        Assert.Equal(80, fromRight.LeftBattery.Value);
        Assert.Equal(70, fromRight.RightBattery.Value);
    }

    [Fact]
    public void Earbuds_UnknownNibbleIsUnknownNotZero()
    {
        var status = ProximityPairingParser.Parse(Build(0x14, 0x20, 0x20, 0xF8, 0x0F))!;
        Assert.Equal(80, status.LeftBattery.Value);
        Assert.False(status.RightBattery.HasValue);
        Assert.False(status.CaseBattery.HasValue);
        Assert.Equal(80, status.LowestBattery.Value);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 50)]
    [InlineData(10, 100)]
    [InlineData(11, null)] // sources disagree on 0xB-0xE -> unknown
    [InlineData(15, null)] // 0xF = unknown/disconnected
    public void NibbleToPercent(int nibble, int? expected) =>
        Assert.Equal(expected, ProximityPairingParser.NibbleToPercent(nibble));

    [Fact]
    public void UnknownModel_KeepsEveryFieldUnknown()
    {
        var status = ProximityPairingParser.Parse(Build(0x99, 0x99, 0x22, 0x55, 0x55))!;
        Assert.Null(status.Profile);
        Assert.False(status.Battery.HasValue);
        Assert.False(status.LeftBattery.HasValue);
        Assert.False(status.AnyWorn.HasValue);
        Assert.StartsWith("Unknown Apple audio device", status.ModelName);
    }

    [Theory]
    [InlineData("0719001F2001038000120000000000000000000000000000000000")] // d[2]=00 pairing mode (LibrePods skips)
    [InlineData("0719011F20010380001200")]                                // truncated
    [InlineData("1005011F2001038000120000000000000000000000000000000000")] // other message type
    public void RejectsNonStatusFrames(string hex) => Assert.Null(ProximityPairingParser.Parse(Hex.Parse(hex)));

    [Fact]
    public void VerifiedLayoutOverridesConfidence()
    {
        var verified = ModelRegistry.WithLayout(Bound, new HeadphoneLayout(
            new NibbleRule(6, false, Confidence.VerifiedOnDevice, "test"),
            null,
            new BitRule(5, 0x02, true, Confidence.VerifiedOnDevice, "test")));

        var status = ProximityPairingParser.Parse(Build(0x1F, 0x20, 0x02, 0x07, 0x00), _ => verified)!;
        Assert.Equal(70, status.Battery.Value);
        Assert.True(status.Battery.IsUsable(Confidence.VerifiedOnDevice));
        Assert.True(status.Worn.Value);
        Assert.False(status.Charging.HasValue);
    }

    [Fact]
    public void WithoutBinding_MaxUsbCIsNotRecognised()
    {
        // No hardcoded entry: until the user binds their device, its frames are an unknown model.
        var status = ProximityPairingParser.Parse(Hex.Parse("0719011F200103800012C400000000000000000000000000000000"))!;
        Assert.Null(status.Profile);
        Assert.False(status.Battery.HasValue);
    }

    [Theory]
    [InlineData((ushort)0x201F, (ushort)0x1F20)]
    [InlineData((ushort)0x2014, (ushort)0x1420)]
    public void ProductIdMapsToAdvertisedModelBytes(ushort productId, ushort advertised) =>
        Assert.Equal(advertised, BoundDeviceProfile.AdvertisedModelId(productId));

    [Theory]
    [InlineData(0x240418u, true)]  // Audio/Video, minor 0x06 Headphones
    [InlineData(0x240404u, false)] // Audio/Video, minor 0x01 Wearable headset
    [InlineData(0x5A020Cu, false)] // Phone
    public void HeadphonesClassOfDevice(uint cod, bool expected) =>
        Assert.Equal(expected, BoundDeviceProfile.IsHeadphonesClass(cod));

    [Fact]
    public void BindingIgnoresNonAppleDevices() =>
        Assert.Null(BoundDeviceProfile.Create("Other", 0x02B0, 0x0000, 0x240404));

    private static byte[] Build(byte modelHi, byte modelLo, byte status, byte battery, byte chargeCase)
    {
        var p = new byte[27];
        p[0] = 0x07;
        p[1] = 0x19;
        p[2] = 0x01;
        p[3] = modelHi;
        p[4] = modelLo;
        p[5] = status;
        p[6] = battery;
        p[7] = chargeCase;
        return p;
    }
}
