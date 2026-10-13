using Orbit.Core.Parsing;

namespace Orbit.Core.Crypto;

public enum KeyState
{
    /// <summary>No key entered.</summary>
    None,

    /// <summary>Key entered, not enough advertisements seen yet to judge it.</summary>
    Checking,

    /// <summary>Decrypts consistently and agrees with the verified coarse battery reading.</summary>
    Confirmed,

    /// <summary>Did not produce plausible values: wrong key, or a key for different headphones.</summary>
    Rejected,
}

/// <summary>Decrypted battery for one advertisement.</summary>
public sealed record ExactBattery(int Level, bool Charging);

/// <summary>
/// Decrypts the encrypted tail with the user's key and decides whether to trust it.
///
/// Plaintext layout (LibrePods linux/battery.hpp parseEncryptedPacket L157-217; CAPod ApplePods.kt):
/// bytes 1, 2, 3 = battery components, each <c>level = b &amp; 0x7F</c> (percent), <c>charging = b &amp; 0x80</c>,
/// level 0x7F = component not present. For headphones (AirPods Max) LibrePods uses the first of bytes 1..3
/// that is present; CAPod uses byte 1. No source has a checksum, so Orbit validates instead:
/// every component must be ≤ 100 or 0x7F, at least one present, and the level must fall inside the
/// verified coarse range from the unencrypted nibble (e.g. coarse 30 → 28..41). The key is confirmed after
/// <see cref="ConfirmAfter"/> consistent frames and rejected after <see cref="RejectAfter"/> frames without
/// one. Both byte orders of the entered key are tried, since sources disagree on whether keys are reversed.
/// </summary>
public sealed class ExactBatteryResolver
{
    public const int ConfirmAfter = 3;
    public const int RejectAfter = 12;

    private readonly byte[][] _candidates;
    private readonly int[] _good;
    private int _frames;
    private int _chosen = -1;

    public ExactBatteryResolver(byte[]? key)
    {
        _candidates = key is { Length: 16 } ? [key, key.Reverse().ToArray()] : [];
        _good = new int[_candidates.Length];
        State = _candidates.Length == 0 ? KeyState.None : KeyState.Checking;
    }

    public KeyState State { get; private set; }

    /// <summary>True when the confirmed key was the reversed byte order of what the user entered.</summary>
    public bool UsedReversedKey => _chosen == 1;

    /// <summary>
    /// Returns the exact battery for a headphone frame once the key is confirmed (otherwise null).
    /// <paramref name="coarse"/> is the verified 10 %-step reading from the same frame.
    /// </summary>
    public ExactBattery? Resolve(ProximityPairingFrame frame, Reading<int> coarse)
    {
        if (State is KeyState.None or KeyState.Rejected || frame.EncryptedTail.Length != 16)
        {
            return null;
        }

        if (State == KeyState.Confirmed)
        {
            var value = Decode(ProximityKey.DecryptBlock(frame.EncryptedTail, _candidates[_chosen]), coarse);
            return value;
        }

        _frames++;
        for (var i = 0; i < _candidates.Length; i++)
        {
            if (Decode(ProximityKey.DecryptBlock(frame.EncryptedTail, _candidates[i]), coarse) is not null && ++_good[i] >= ConfirmAfter)
            {
                _chosen = i;
                State = KeyState.Confirmed;
                return Decode(ProximityKey.DecryptBlock(frame.EncryptedTail, _candidates[i]), coarse);
            }
        }

        if (_frames >= RejectAfter && _good.All(g => g == 0))
        {
            State = KeyState.Rejected;
        }

        return null;
    }

    /// <summary>Exact battery from a plaintext block, or null if it fails validation.</summary>
    internal static ExactBattery? Decode(byte[] plain, Reading<int> coarse)
    {
        var components = new[] { plain[1], plain[2], plain[3] };
        if (components.Any(b => (b & 0x7F) > 100 && (b & 0x7F) != 0x7F))
        {
            return null;
        }

        var present = components.Where(b => (b & 0x7F) != 0x7F).ToList();
        if (present.Count == 0)
        {
            return null;
        }

        var level = present[0] & 0x7F;
        var charging = (present[0] & 0x80) != 0;

        // Must agree with the verified coarse value (nibble × 10, floor): e.g. Mac 37 % was advertised as 3.
        if (coarse.HasValue && (level < coarse.Value - 2 || level > Math.Min(100, coarse.Value + 11)))
        {
            return null;
        }

        return new ExactBattery(level, charging);
    }
}
