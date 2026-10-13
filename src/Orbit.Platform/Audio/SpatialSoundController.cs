using Windows.Devices.Enumeration;
using Windows.Media.Audio;
using Windows.Media.Devices;

namespace Orbit.Platform.Audio;

/// <summary>A Windows spatial sound format that can be applied to an output device.</summary>
public sealed record SpatialFormat(string Name, string Subtype);

/// <summary>Spatial sound state of one audio output endpoint.</summary>
public sealed record SpatialSoundState(
    string EndpointId,
    string EndpointName,
    bool Supported,
    string? ActiveSubtype,
    IReadOnlyList<SpatialFormat> AvailableFormats);

/// <summary>
/// Windows' own spatial sound (Windows Sonic for Headphones, Dolby Atmos, DTS) for the headphones' render
/// endpoint, via the public Windows.Media.Audio.SpatialAudioDeviceConfiguration API (Windows 10 2004+).
/// This is NOT Apple's Spatial Audio: head-tracked Spatial Audio is rendered by the Apple source device using
/// the headphones' motion data over AACP, which Windows cannot reach (docs/noise-control.md).
/// </summary>
public static class SpatialSoundController
{
    private const string ContainerIdKey = "System.Devices.ContainerId";

    private static readonly SpatialFormat[] KnownFormats =
    [
        new("Windows Sonic for Headphones", SpatialAudioFormatSubtype.WindowsSonic),
        new("Dolby Atmos for Headphones", SpatialAudioFormatSubtype.DolbyAtmosForHeadphones),
        new("Dolby Atmos for Home Theater", SpatialAudioFormatSubtype.DolbyAtmosForHomeTheater),
        new("DTS Headphone:X", SpatialAudioFormatSubtype.DTSHeadphoneX),
        new("DTS:X", SpatialAudioFormatSubtype.DTSXUltra),
    ];

    /// <summary>
    /// Finds the active render endpoint(s) belonging to the paired device (matched by ContainerId) and returns
    /// their spatial sound state. A Bluetooth headset usually has a "Headphones" (A2DP) endpoint; that is the
    /// one that matters for music.
    /// </summary>
    public static async Task<IReadOnlyList<SpatialSoundState>> GetForContainerAsync(Guid containerId)
    {
        var endpoints = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector(), [ContainerIdKey]);
        var result = new List<SpatialSoundState>();
        foreach (var ep in endpoints)
        {
            if (!ep.Properties.TryGetValue(ContainerIdKey, out var c) || c is not Guid g || g != containerId)
            {
                continue;
            }

            var config = SpatialAudioDeviceConfiguration.GetForDeviceId(ep.Id);
            var formats = KnownFormats.Where(f => config.IsSpatialAudioFormatSupported(f.Subtype)).ToList();
            result.Add(new SpatialSoundState(
                ep.Id,
                ep.Name,
                config.IsSpatialAudioSupported,
                IsOff(config.ActiveSpatialAudioFormat) ? null : config.ActiveSpatialAudioFormat,
                formats));
        }

        return result;
    }

    /// <summary>Sets the endpoint's default spatial format. Returns the API's status text ("Succeeded" on success).</summary>
    public static async Task<string> SetAsync(string endpointId, string subtype)
    {
        var config = SpatialAudioDeviceConfiguration.GetForDeviceId(endpointId);
        var result = await config.SetDefaultSpatialAudioFormatAsync(subtype);
        return result.Status.ToString();
    }

    /// <summary>
    /// Windows reports "no spatial format" as an empty string or the all-zero GUID
    /// ({00000000-0000-0000-0000-000000000000}, seen on the owner's PC). Both mean Off.
    /// </summary>
    public const string OffSubtype = "{00000000-0000-0000-0000-000000000000}";

    public static bool IsOff(string? subtype) =>
        string.IsNullOrEmpty(subtype) || (Guid.TryParse(subtype, out var g) && g == Guid.Empty);

    public static string NameOf(string? subtype) =>
        IsOff(subtype) ? "Off" : KnownFormats.FirstOrDefault(f => string.Equals(f.Subtype, subtype, StringComparison.OrdinalIgnoreCase))?.Name ?? subtype!;

    /// <summary>Reads the active format of one endpoint (null = Off).</summary>
    public static string? GetActive(string endpointId)
    {
        var active = SpatialAudioDeviceConfiguration.GetForDeviceId(endpointId).ActiveSpatialAudioFormat;
        return IsOff(active) ? null : active;
    }
}
