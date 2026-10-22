using System.Runtime.InteropServices;

namespace Orbit.Platform.Bluetooth.Interop;

// Minimal Core Audio / Kernel Streaming COM declarations (Windows SDK mmdeviceapi.h, devicetopology.h,
// ks.h, ksproxy.h, propsys.h). Vtable order must match the headers exactly; only the methods Orbit calls are
// typed, the rest are placeholders to keep slot positions.

internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

internal static class DeviceState
{
    public const uint Active = 0x00000001; // DEVICE_STATE_ACTIVE
    public const uint All = 0x0000000F; // DEVICE_STATEMASK_ALL
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, int role, out IMMDevice endpoint);

    [PreserveSig]
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    [PreserveSig]
    int RegisterEndpointNotificationCallback(IntPtr client);

    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int Item(uint index, out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    [PreserveSig]
    int OpenPropertyStore(uint stgmAccess, out IPropertyStore properties);

    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig]
    int GetState(out uint state);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetAt(uint index, out PropertyKey key);

    [PreserveSig]
    int GetValue(ref PropertyKey key, out PropVariant value);

    [PreserveSig]
    int SetValue(ref PropertyKey key, ref PropVariant value);

    [PreserveSig]
    int Commit();
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid fmtid, uint pid)
{
    public Guid FormatId = fmtid;
    public uint PropertyId = pid;

    // propkey.h / functiondiscoverykeys_devpkey.h
    public static PropertyKey DeviceContainerId => new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
    public static PropertyKey DeviceFriendlyName => new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    // mmdeviceapi.h PKEY_AudioEndpoint_FormFactor (VT_UI4, EndpointFormFactor).
    public static PropertyKey AudioEndpointFormFactor => new(new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), 0);
}

/// <summary>PROPVARIANT subset: VT_UI4 (19), VT_LPWSTR (31) and VT_CLSID (72) are all Orbit reads.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort VarType;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public IntPtr Pointer;
    public IntPtr Pointer2;

    public readonly string? AsString() => VarType == 31 ? Marshal.PtrToStringUni(Pointer) : null;

    /// <summary>VT_UI4 sits in the first 4 bytes of the value union (little-endian).</summary>
    public readonly uint? AsUInt32() => VarType == 19 ? unchecked((uint)Pointer.ToInt64()) : null;

    public readonly Guid? AsGuid() => VarType == 72 && Pointer != IntPtr.Zero ? Marshal.PtrToStructure<Guid>(Pointer) : null;
}

internal static class PropVariantNative
{
    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant pvar);
}

[ComImport]
[Guid("2A07407E-6497-4A18-9787-32F79BD0D98F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeviceTopology
{
    [PreserveSig]
    int GetConnectorCount(out uint count);

    [PreserveSig]
    int GetConnector(uint index, out IConnector connector);

    [PreserveSig]
    int GetSubunitCount(out uint count);

    [PreserveSig]
    int GetSubunit(uint index, out IntPtr subunit);

    [PreserveSig]
    int GetPartById(uint id, out IntPtr part);

    [PreserveSig]
    int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig]
    int GetSignalPath(IntPtr from, IntPtr to, [MarshalAs(UnmanagedType.Bool)] bool rejectMixedPaths, out IntPtr parts);
}

[ComImport]
[Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IConnector
{
    [PreserveSig]
    int GetType(out int type);

    [PreserveSig]
    int GetDataFlow(out int flow);

    [PreserveSig]
    int ConnectTo(IConnector connectTo);

    [PreserveSig]
    int Disconnect();

    [PreserveSig]
    int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);

    [PreserveSig]
    int GetConnectedTo(out IConnector connectedTo);

    [PreserveSig]
    int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);

    [PreserveSig]
    int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
}

/// <summary>KSIDENTIFIER / KSPROPERTY (ks.h): GUID Set, ULONG Id, ULONG Flags; 8-byte aligned union, 24 bytes.</summary>
[StructLayout(LayoutKind.Sequential, Size = 24)]
internal struct KsProperty(Guid set, uint id, uint flags)
{
    public Guid Set = set;
    public uint Id = id;
    public uint Flags = flags;

    public const uint TypeGet = 0x00000001; // KSPROPERTY_TYPE_GET
}

[ComImport]
[Guid("28F54685-06FD-11D2-B27A-00A0C9223196")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IKsControl
{
    [PreserveSig]
    int KsProperty(ref KsProperty property, uint propertyLength, IntPtr data, uint dataLength, out uint bytesReturned);

    [PreserveSig]
    int KsMethod(IntPtr method, uint methodLength, IntPtr data, uint dataLength, out uint bytesReturned);

    [PreserveSig]
    int KsEvent(IntPtr @event, uint eventLength, IntPtr data, uint dataLength, out uint bytesReturned);
}
