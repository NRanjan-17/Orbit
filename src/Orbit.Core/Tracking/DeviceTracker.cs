using Orbit.Core.Parsing;

namespace Orbit.Core.Tracking;

/// <summary>The device Orbit is currently showing.</summary>
public sealed record TrackedDevice(DeviceStatus Status, double SmoothedRssi, short LastRssi, DateTimeOffset LastSeen);

/// <summary>
/// Picks "my" device out of all Proximity Pairing traffic. Apple devices rotate their BLE address, so
/// devices are matched by payload identity (model + colour, <see cref="ProximityPairingFrame.IdentityKey"/>),
/// never by address. Frames below the RSSI threshold are ignored so a colleague's headphones across the room
/// are not shown.
/// </summary>
public sealed class DeviceTracker
{
    private readonly Dictionary<string, TrackedDevice> _candidates = [];
    private readonly Dictionary<string, ChargingLatch> _latches = [];
    private readonly Func<ushort, ModelProfile?> _lookup;
    private readonly ushort? _boundModelId;

    /// <param name="bound">The user's bound device profile, or null to show the nearest known device.</param>
    public DeviceTracker(ModelProfile? bound, int rssiThreshold = -70, TimeSpan? staleAfter = null)
    {
        _boundModelId = bound?.ModelId;
        _lookup = ModelRegistry.WithBound(bound);
        RssiThreshold = rssiThreshold;
        StaleAfter = staleAfter ?? TimeSpan.FromSeconds(20);
    }

    /// <summary>Minimum RSSI (dBm) for a frame to count. Typical: -50 next to you, -80 across a room.</summary>
    public int RssiThreshold { get; set; }

    public TimeSpan StaleAfter { get; }

    public TrackedDevice? Current { get; private set; }

    /// <summary>Feeds one advertisement. Returns the parsed status if it was accepted for the current device.</summary>
    public DeviceStatus? Add(ReadOnlySpan<byte> payload, short rssi, DateTimeOffset at)
    {
        var status = ProximityPairingParser.Parse(payload, _lookup);
        if (status is null || rssi < RssiThreshold)
        {
            return null;
        }

        // With a bound device, ignore every other model entirely.
        if (_boundModelId is { } bound && status.ModelId != bound)
        {
            return null;
        }

        // Unknown models are never shown (nothing about them can be decoded with confidence).
        if (status.Profile is null)
        {
            return null;
        }

        var key = status.Frame.IdentityKey;
        if (!_latches.TryGetValue(key, out var latch))
        {
            _latches[key] = latch = new ChargingLatch();
        }

        status = status with { Charging = latch.Update(status.Charging, at) };
        var smoothed = _candidates.TryGetValue(key, out var prev) ? prev.SmoothedRssi * 0.7 + rssi * 0.3 : rssi;
        var tracked = new TrackedDevice(status, smoothed, rssi, at);
        _candidates[key] = tracked;

        Prune(at);

        // Stick with the current device unless another one is clearly closer (6 dB hysteresis).
        var best = _candidates.Values.MaxBy(c => c.SmoothedRssi)!;
        if (Current is null
            || Current.Status.Frame.IdentityKey == key
            || !_candidates.ContainsKey(Current.Status.Frame.IdentityKey)
            || best.SmoothedRssi > _candidates[Current.Status.Frame.IdentityKey].SmoothedRssi + 6)
        {
            Current = Current?.Status.Frame.IdentityKey == key ? tracked : best;
        }

        return Current.Status.Frame.IdentityKey == key ? status : null;
    }

    /// <summary>Drops devices not heard from recently. Returns true if the current device was lost.</summary>
    public bool Prune(DateTimeOffset now)
    {
        foreach (var k in _candidates.Where(kv => now - kv.Value.LastSeen > StaleAfter).Select(kv => kv.Key).ToList())
        {
            _candidates.Remove(k);
        }

        if (Current is not null && !_candidates.ContainsKey(Current.Status.Frame.IdentityKey))
        {
            Current = null;
            return true;
        }

        return false;
    }
}
