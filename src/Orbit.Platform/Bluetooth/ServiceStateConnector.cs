using System.Runtime.InteropServices;
using Orbit.Core.Switching;

namespace Orbit.Platform.Bluetooth;

/// <summary>
/// Fallback connector using Win32 BluetoothSetServiceState (bthprops.cpl) to toggle the A2DP sink and
/// Hands-Free services. Microsoft: "Enabling a service installs the corresponding device driver and disabling a
/// service removes the corresponding device driver." So "connect" = disable + enable (driver reinstall, forcing
/// Windows to page the device) and "disconnect" = disable. It is slow (seconds) and churns drivers; ToothTray
/// abandoned it for the KS method (docs/sources.md S3). Kept as a user-selectable fallback.
/// </summary>
public sealed class ServiceStateConnector(ulong address, string name) : IAudioConnector
{
    // bthdef.h short UUIDs on the Bluetooth base UUID 0000xxxx-0000-1000-8000-00805F9B34FB.
    private static readonly Guid AudioSink = new("0000110B-0000-1000-8000-00805F9B34FB");
    private static readonly Guid Handsfree = new("0000111E-0000-1000-8000-00805F9B34FB");

    private const uint ServiceDisable = 0x00; // BLUETOOTH_SERVICE_DISABLE
    private const uint ServiceEnable = 0x01;  // BLUETOOTH_SERVICE_ENABLE

    public string Name => "Toggle Bluetooth services (BluetoothSetServiceState)";

    public string LastDiagnostics { get; private set; } = "";

    /// <summary>Marshalled size of BLUETOOTH_DEVICE_INFO; must be 560 on 64-bit Windows.</summary>
    public static int DeviceInfoSize => Marshal.SizeOf<NativeMethods.BluetoothDeviceInfo>();

    public Task<bool> RequestConnectAsync(CancellationToken ct) => Task.Run(() =>
    {
        var log = new List<string>();
        Set(ServiceDisable, log);
        Thread.Sleep(500);
        var ok = Set(ServiceEnable, log);
        LastDiagnostics = string.Join(Environment.NewLine, log);
        return ok;
    }, ct);

    public Task<bool> RequestDisconnectAsync(CancellationToken ct) => Task.Run(() =>
    {
        var log = new List<string>();
        var ok = Set(ServiceDisable, log);
        LastDiagnostics = string.Join(Environment.NewLine, log);
        return ok;
    }, ct);

    private bool Set(uint flags, List<string> log)
    {
        var info = NativeMethods.BluetoothDeviceInfo.Create(address, name);
        var ok = true;
        foreach (var (service, label) in new[] { (AudioSink, "A2DP sink"), (Handsfree, "Hands-free") })
        {
            var guid = service;
            var err = NativeMethods.BluetoothSetServiceState(IntPtr.Zero, ref info, ref guid, flags);
            log.Add($"BluetoothSetServiceState({label}, {(flags == ServiceEnable ? "enable" : "disable")}) = {err}");

            // E_INVALIDARG (0x80070057 / 87) is returned when the service is already in the requested state.
            ok &= err is 0 or 87 or unchecked((uint)0x80070057);
        }

        return ok;
    }

    internal static class NativeMethods
    {
        public const int MaxNameSize = 248; // BLUETOOTH_MAX_NAME_SIZE

        [DllImport("bthprops.cpl", ExactSpelling = true, SetLastError = true)]
        public static extern uint BluetoothSetServiceState(IntPtr hRadio, ref BluetoothDeviceInfo pbtdi, ref Guid pGuidService, uint dwServiceFlags);

        [StructLayout(LayoutKind.Sequential)]
        public struct SystemTime
        {
            public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
        }

        /// <summary>BLUETOOTH_DEVICE_INFO (bluetoothapis.h L268-287). Expected size 560 bytes on x64/ARM64.</summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct BluetoothDeviceInfo
        {
            public uint Size;
            public ulong Address; // BLUETOOTH_ADDRESS union (ullLong); 8-byte aligned -> 4 bytes padding after Size
            public uint ClassOfDevice;
            public int Connected;
            public int Remembered;
            public int Authenticated;
            public SystemTime LastSeen;
            public SystemTime LastUsed;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxNameSize)]
            public string Name;

            public static BluetoothDeviceInfo Create(ulong address, string name) => new()
            {
                Size = (uint)Marshal.SizeOf<BluetoothDeviceInfo>(),
                Address = address,
                Name = name,
            };
        }
    }
}
