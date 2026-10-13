using System.Security.Cryptography;
using Orbit.Core.Crypto;
using Orbit.Core.Parsing;
using Orbit.Core.Tracking;

namespace Orbit.Tests.Crypto;

/// <summary>
/// Exact-battery decryption with a SYNTHETIC key and plaintext (the owner's real key must never be in the repo).
/// Exercises the algorithm from LibrePods/CAPod: AES-128 one block, components at plaintext bytes 1..3.
/// Whether the real Max uses this layout is confirmed only when the owner enters their key.
/// </summary>
public class ExactBatteryTests
{
    private static readonly byte[] Key = Convert.FromHexString("000102030405060708090A0B0C0D0E0F");
    private static readonly ModelProfile Bound = BoundDeviceProfile.Create("My headphones", 0x004C, 0x201F, 0x240418)!;
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static byte[] Encrypt(byte[] plain, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptEcb(plain, PaddingMode.None);
    }

    /// <summary>Builds a Max frame: coarse nibble <paramref name="coarse"/>, encrypted tail from <paramref name="level"/>.</summary>
    private static int _nonce;

    private static byte[] Frame(int coarse, int level, bool charging, byte[] key)
    {
        var plain = new byte[16];
        plain[4] = (byte)Interlocked.Increment(ref _nonce); // real tails change every few seconds; so do ours
        plain[1] = (byte)(level | (charging ? 0x80 : 0));
        plain[2] = 0x7F; // not present
        plain[3] = 0x7F;
        var p = new byte[27];
        p[0] = 0x07; p[1] = 0x19; p[2] = 0x01; p[3] = 0x1F; p[4] = 0x20; p[5] = 0x01; p[6] = (byte)coarse; p[7] = 0x80; p[9] = 0x12;
        Encrypt(plain, key).CopyTo(p, 11);
        return p;
    }

    [Theory]
    [InlineData("000102030405060708090A0B0C0D0E0F")]
    [InlineData("00-01-02-03-04-05-06-07-08-09-0A-0B-0C-0D-0E-0F")]
    [InlineData("<dict><key>MagicAccIRK</key><string>FF-FF-FF-FF-FF-FF-FF-FF-FF-FF-FF-FF-FF-FF-FF-FF</string><key>MagicAccEncKey</key><string>00-01-02-03-04-05-06-07-08-09-0A-0B-0C-0D-0E-0F</string></dict>")]
    public void ParsesKeyFormats(string text)
    {
        Assert.True(ProximityKey.TryParse(text, out var key));
        Assert.Equal(Key, key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("3A-9C")]
    [InlineData("<key>MagicAccIRK</key><string>00</string>")]
    public void RejectsBadKeyText(string text) => Assert.False(ProximityKey.TryParse(text, out _));

    [Fact]
    public void ConfirmsKeyAndReportsExactLevel()
    {
        var tracker = new DeviceTracker(Bound) { Exact = new ExactBatteryResolver(Key) };
        DeviceStatus? last = null;
        for (var i = 0; i < ExactBatteryResolver.ConfirmAfter; i++)
        {
            last = tracker.Add(Frame(coarse: 6, level: 64, charging: false, Key), -40, T0.AddSeconds(i));
        }

        Assert.Equal(KeyState.Confirmed, tracker.Exact!.State);
        Assert.Equal(64, last!.ExactBattery.Value);
        Assert.Equal(Confidence.Corroborated, last.ExactBattery.Confidence);
        Assert.Equal(60, last.Battery.Value); // coarse reading unchanged
    }

    [Fact]
    public void RememberedKeyShowsExactBatteryImmediately()
    {
        var tracker = new DeviceTracker(Bound) { Exact = new ExactBatteryResolver(Key, previouslyConfirmedReversed: false) };
        var status = tracker.Add(Frame(6, 64, false, Key), -40, T0);
        Assert.Equal(KeyState.Confirmed, tracker.Exact!.State);
        Assert.Equal(64, status!.ExactBattery.Value); // first broadcast, no 3-broadcast wait
    }

    [Fact]
    public void RememberedKeyThatStopsWorkingIsCheckedAgain()
    {
        var other = Convert.FromHexString("F0E0D0C0B0A090807060504030201000");
        var tracker = new DeviceTracker(Bound) { Exact = new ExactBatteryResolver(other, previouslyConfirmedReversed: false) };
        for (var i = 0; i < ExactBatteryResolver.RecheckAfterFailures; i++)
        {
            Assert.False(tracker.Add(Frame(6, 64, false, Key), -40, T0.AddSeconds(i))!.ExactBattery.HasValue);
        }

        Assert.Equal(KeyState.Checking, tracker.Exact!.State);
    }

    [Fact]
    public void RepeatedCiphertextIsNotNewEvidence()
    {
        // The bug seen on the real device: one ciphertext repeated must never confirm a key.
        var frame = Frame(6, 64, false, Key);
        var tracker = new DeviceTracker(Bound) { Exact = new ExactBatteryResolver(Key) };
        for (var i = 0; i < 10; i++)
        {
            tracker.Add(frame, -40, T0.AddSeconds(i));
        }

        Assert.Equal(KeyState.Checking, tracker.Exact!.State);
    }

    [Fact]
    public void WrongKeyIsRejectedAndNeverShowsAValue()
    {
        var other = Convert.FromHexString("F0E0D0C0B0A090807060504030201000");
        var tracker = new DeviceTracker(Bound) { Exact = new ExactBatteryResolver(other) };
        for (var i = 0; i < ExactBatteryResolver.RejectAfter + 2; i++)
        {
            var status = tracker.Add(Frame(6, 64, false, Key), -40, T0.AddSeconds(i));
            Assert.False(status!.ExactBattery.HasValue);
        }

        Assert.Equal(KeyState.Rejected, tracker.Exact!.State);
    }

    [Fact]
    public void ReversedKeyOrderIsDetected()
    {
        var reversed = Key.Reverse().ToArray();
        var resolver = new ExactBatteryResolver(reversed); // user entered it backwards
        var tracker = new DeviceTracker(Bound) { Exact = resolver };
        for (var i = 0; i < ExactBatteryResolver.ConfirmAfter; i++)
        {
            tracker.Add(Frame(6, 61, true, Key), -40, T0.AddSeconds(i));
        }

        Assert.Equal(KeyState.Confirmed, resolver.State);
        Assert.True(resolver.UsedReversedKey);
    }

    [Theory]
    // Real decrypted blocks from the owner's AirPods Max (USB-C), 2026-10-14 (protocol-findings F7). Plaintext
    // only, and bytes 4-6 (part of the connected host's Bluetooth address, F11) replaced with AA. Bytes 0-10 constant,
    // byte 11 counts, 12-15 vary.
    [InlineData("04410000AAAAAA00000000013AE4C939")]
    [InlineData("04410000AAAAAA0000000002EDCF7E3F")]
    public void RealMaxPlaintextDecodesTo65Percent(string plainHex)
    {
        var coarse = new Reading<int>(60, Confidence.VerifiedOnDevice, "t"); // advertised nibble 6
        var exact = ExactBatteryResolver.Decode(Convert.FromHexString(plainHex), coarse);
        Assert.Equal(new ExactBattery(65, false), exact); // byte 1 = 0x41 = 65 %
    }

    [Fact]
    public void DecryptedValueMustAgreeWithVerifiedCoarseReading()
    {
        // Plausible-looking plaintext (64 %) but the coarse nibble says 30-39 %: rejected.
        var coarse = new Reading<int>(30, Confidence.VerifiedOnDevice, "t");
        var plain = new byte[16];
        plain[1] = 64; plain[2] = 0x7F; plain[3] = 0x7F;
        Assert.Null(ExactBatteryResolver.Decode(plain, coarse));

        plain[1] = 37;
        Assert.Equal(new ExactBattery(37, false), ExactBatteryResolver.Decode(plain, coarse));
    }
}
