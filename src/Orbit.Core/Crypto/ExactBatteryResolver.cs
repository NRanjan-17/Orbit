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
/// <see cref="ConfirmAfter"/> DIFFERENT encrypted tails in a row decode to levels within ±2 of each other
/// (a wrong key yields unrelated values for each new ciphertext), and rejected after <see cref="RejectAfter"/>
/// distinct tails without that. Both byte orders of the entered key are tried.
/// </summary>
public sealed class ExactBatteryResolver
{
    public const int ConfirmAfter = 3;
    public const int RejectAfter = 12;

    private readonly byte[][] _candidates;
    private readonly List<int>[] _levels;
    private readonly HashSet<string> _seenTails = [];
    private int _frames;
    private int _chosen = -1;

    public ExactBatteryResolver(byte[]? key)
    {
        _candidates = key is { Length: 16 } ? [key, key.Reverse().ToArray()] : [];
        _levels = _candidates.Select(_ => new List<int>()).ToArray();
        State = _candidates.Length == 0 ? KeyState.None : KeyState.Checking;
    }

    public KeyState State { get; private set; }

    /// <summary>True when the confirmed key was the reversed byte order of what the user entered.</summary>
    public bool UsedReversedKey => _chosen == 1;

    /// <summary>
    /// Diagnostics while checking: plaintext bytes 1..3 decrypted with each key order for the last frame
    /// (battery components only; never the key). Empty once confirmed.
    /// </summary>
    public string LastCheckDiagnostics { get; private set; } = "";

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

        // The headphones repeat the same encrypted tail for several seconds; only a NEW tail is new evidence.
        // (First version counted repeats and "confirmed" a key from one ciphertext seen three times.)
        var tailHex = Convert.ToHexString(frame.EncryptedTail);
        if (!_seenTails.Add(tailHex))
        {
            return null;
        }

        _frames++;
        LastCheckDiagnostics = string.Join("  ", _candidates.Select((k, i) =>
        {
            var p = ProximityKey.DecryptBlock(frame.EncryptedTail, k);
            return $"{(i == 0 ? "as-entered" : "reversed")}: [1]={p[1]:X2} [2]={p[2]:X2} [3]={p[3]:X2}";
        })) + $"  coarse={(coarse.HasValue ? coarse.Value : -1)}  distinct tails={_frames}";

        for (var i = 0; i < _candidates.Length; i++)
        {
            var decoded = Decode(ProximityKey.DecryptBlock(frame.EncryptedTail, _candidates[i]), coarse);
            if (decoded is null)
            {
                _levels[i].Clear();
                continue;
            }

            // A wrong key turns each new ciphertext into unrelated noise; the right key gives a steady level.
            if (_levels[i].Count > 0 && Math.Abs(decoded.Level - _levels[i][^1]) > 2)
            {
                _levels[i].Clear();
            }

            _levels[i].Add(decoded.Level);
            if (_levels[i].Count >= ConfirmAfter)
            {
                _chosen = i;
                State = KeyState.Confirmed;
                return decoded;
            }
        }

        if (_frames >= RejectAfter)
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
