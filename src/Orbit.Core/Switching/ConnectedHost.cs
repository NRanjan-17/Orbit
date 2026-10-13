using Orbit.Core.Parsing;

namespace Orbit.Core.Switching;

public enum ConnectedTo
{
    /// <summary>No fresh decrypted broadcast (no key, asleep) or no local adapter address.</summary>
    Unknown,

    /// <summary>The host bytes match this PC's Bluetooth adapter.</summary>
    ThisPc,

    /// <summary>The host bytes are a different, non-zero address: e.g. the owner's Mac.</summary>
    OtherDevice,
}

/// <summary>
/// Tells "connected to this PC" from "connected to another device" using the connected-host bytes from the
/// headphones' decrypted broadcast (protocol-findings F11). An all-zero value is treated as unknown: its meaning
/// (e.g. not connected to anything) has not been captured.
/// </summary>
public static class ConnectedHost
{
    public static ConnectedTo Classify(Reading<int> hostSuffix, int? localSuffix)
    {
        if (!hostSuffix.HasValue || hostSuffix.Value < 0 || localSuffix is null || hostSuffix.Value == 0)
        {
            return ConnectedTo.Unknown;
        }

        return hostSuffix.Value == localSuffix.Value ? ConnectedTo.ThisPc : ConnectedTo.OtherDevice;
    }
}
