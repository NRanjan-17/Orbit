using System.Runtime.InteropServices;

namespace Orbit.Platform.Audio.Interop;

// IPolicyConfig is an UNDOCUMENTED Windows interface (the one the Sound control panel uses to change the default
// device). Windows has no public API for this; the interface has been stable since Windows 7 and is used by many
// audio tools. Only SetDefaultEndpoint is called; the other slots are placeholders that keep vtable positions.

[ComImport]
[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class PolicyConfigClient
{
}

[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat();

    [PreserveSig] int GetDeviceFormat();

    [PreserveSig] int ResetDeviceFormat();

    [PreserveSig] int SetDeviceFormat();

    [PreserveSig] int GetProcessingPeriod();

    [PreserveSig] int SetProcessingPeriod();

    [PreserveSig] int GetShareMode();

    [PreserveSig] int SetShareMode();

    [PreserveSig] int GetPropertyValue();

    [PreserveSig] int SetPropertyValue();

    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);

    [PreserveSig] int SetEndpointVisibility();
}
