using Orbit.Core.Switching;

namespace Orbit.Platform.Bluetooth;

/// <summary>
/// Default connector, chosen from tests on the owner's AirPods Max (USB-C), 2026-10-13 (docs/multi-device.md):
/// <list type="bullet">
/// <item>KS one-shot reconnect connects quickly (3-4 s), even while the Mac is playing.</item>
/// <item>KS one-shot disconnect drops the audio, but Hands-Free reconnects by itself and the link stays up,
/// which keeps the Mac out.</item>
/// <item>Disabling the A2DP/Hands-Free services removes the audio within ~6 s and the link drops within ~40 s.</item>
/// </list>
/// So: release = KS disconnect + disable services; connect = enable services (no-op if already enabled;
/// re-enabling also starts a connection) + KS reconnect.
/// </summary>
public sealed class HybridConnector(Guid containerId, ulong address, string name) : IAudioConnector
{
    private readonly KsAudioConnector _ks = new(containerId);
    private readonly ServiceStateConnector _services = new(address, name);

    public string Name => "Hybrid (audio driver reconnect + Bluetooth service toggle)";

    public string LastDiagnostics => string.Join(Environment.NewLine, new[] { _ks.LastDiagnostics, _services.LastDiagnostics }.Where(s => s.Length > 0));

    public async Task<bool> RequestConnectAsync(CancellationToken ct)
    {
        var enabled = await _services.EnableAsync(ct);
        var ks = await _ks.RequestConnectAsync(ct);
        return enabled || ks;
    }

    public async Task<bool> RequestDisconnectAsync(CancellationToken ct)
    {
        var ks = await _ks.RequestDisconnectAsync(ct);
        var disabled = await _services.RequestDisconnectAsync(ct);
        return ks || disabled;
    }
}
