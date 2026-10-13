using Orbit.Core.Switching;

namespace Orbit.Platform.Bluetooth;

/// <summary>
/// "Connected" = at least one of the device's audio outputs (Headphones / Headset) is active.
/// The Bluetooth link alone is not a good signal: on the owner's AirPods Max, after a KS one-shot disconnect
/// both audio endpoints became unplugged within seconds while the link stayed up for 40+ s
/// (docs/multi-device.md, test log 2026-10-13).
/// </summary>
public sealed class AudioConnectionState(Guid containerId) : IConnectionState
{
    private const uint DeviceStateActive = 1;
    private readonly KsAudioConnector _probe = new(containerId);

    public bool IsConnected => _probe.GetEndpointStates().Any(e => e.State == DeviceStateActive);
}
