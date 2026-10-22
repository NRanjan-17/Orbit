using System.Runtime.InteropServices;
using Orbit.Core.Media;
using Orbit.Platform.Audio.Interop;
using Orbit.Platform.Bluetooth.Interop;

namespace Orbit.Platform.Audio;

/// <summary>
/// The Windows default audio output, via Core Audio (read) and the undocumented IPolicyConfig (write; see
/// PolicyConfigInterop.cs). Used by auto-output to move sound to the PC's speakers while the headphones are off.
/// </summary>
public sealed class WindowsAudioOutputs : IAudioOutputs
{
    public IReadOnlyList<AudioOutput> GetOutputs()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        var result = new List<AudioOutput>();
        if (enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceState.Active, out var devices) != 0)
        {
            return result;
        }

        devices.GetCount(out var count);
        for (uint i = 0; i < count; i++)
        {
            if (devices.Item(i, out var device) != 0 || device.GetId(out var id) != 0)
            {
                continue;
            }

            var name = ReadString(device, PropertyKey.DeviceFriendlyName) ?? id;
            var kind = ReadUInt32(device, PropertyKey.AudioEndpointFormFactor) switch
            {
                1 => AudioOutputKind.Speakers,   // Speakers
                3 or 5 => AudioOutputKind.Headphones, // Headphones, Headset
                _ => AudioOutputKind.Other,
            };
            result.Add(new AudioOutput(id, name, kind, ReadGuid(device, PropertyKey.DeviceContainerId)));
        }

        return result;
    }

    public string? GetDefault(AudioRole role)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        return enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, (int)role, out var device) == 0 && device.GetId(out var id) == 0
            ? id
            : null;
    }

    public bool SetDefault(string endpointId, AudioRole role)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            return policy.SetDefaultEndpoint(endpointId, (int)role) == 0;
        }
        catch (COMException)
        {
            // The undocumented interface is missing or changed on this Windows build.
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
    }

    private static string? ReadString(IMMDevice device, PropertyKey key) => Read<string?>(device, key, v => v.AsString(), null);

    private static uint? ReadUInt32(IMMDevice device, PropertyKey key) => Read<uint?>(device, key, v => v.AsUInt32(), null);

    private static Guid? ReadGuid(IMMDevice device, PropertyKey key) => Read<Guid?>(device, key, v => v.AsGuid(), null);

    private static T Read<T>(IMMDevice device, PropertyKey key, Func<PropVariant, T> convert, T fallback)
    {
        if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) != 0 || store.GetValue(ref key, out var value) != 0)
        {
            return fallback;
        }

        try
        {
            return convert(value);
        }
        finally
        {
            PropVariantNative.PropVariantClear(ref value);
        }
    }
}
