namespace Orbit.Core.Capture;

/// <summary>Standard tag names used by the capture UI and CLI so the analyser can line them up.</summary>
public static class CaptureTags
{
    public const string OnHead = "on-head";
    public const string OffHead = "off-head";
    public const string Charging = "charging";
    public const string NotCharging = "not-charging";
    public const string AncOn = "anc-on";
    public const string Transparency = "transparency";
    public const string NoiseOff = "noise-off";
    public const string ConnectedToMac = "connected-mac";
    public const string ConnectedToPc = "connected-pc";
    public const string Disconnected = "disconnected";
    public const string InCase = "in-case";

    public static IReadOnlyList<string> All { get; } =
    [
        OnHead, OffHead, Charging, NotCharging, AncOn, Transparency, NoiseOff,
        ConnectedToMac, ConnectedToPc, Disconnected, InCase,
    ];
}
