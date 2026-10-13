using System.Runtime.InteropServices;
using Orbit.Core.Switching;
using Orbit.Platform.Bluetooth.Interop;

namespace Orbit.Platform.Bluetooth;

/// <summary>
/// Connects/disconnects a paired Bluetooth audio device by sending KSPROPERTY_ONESHOT_RECONNECT /
/// KSPROPERTY_ONESHOT_DISCONNECT (property set KSPROPSETID_BtAudio) to its Bluetooth audio KS filters, the
/// method used by ToothTray (BluetoothAudioDevices.cpp L39-144) and AirPodsDesktop (QuickConnect_win.cpp
/// L351-449) — docs/sources.md S3. Unlike BluetoothSetServiceState it does not uninstall/reinstall drivers.
/// Microsoft documents that success "does not necessarily mean that the attempt was successful", so callers
/// must verify the connection (SwitchCoordinator does).
/// </summary>
public sealed class KsAudioConnector(Guid containerId) : IAudioConnector
{
    // ksmedia.h: STATIC_KSPROPSETID_BtAudio 0x7FA06C40, 0xB8F6, 0x4C7E, 0x85,0x56,0xE8,0xC3,0x3A,0x12,0xE5,0x4D
    private static readonly Guid KsPropSetIdBtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    private const uint OneShotReconnect = 0; // KSPROPERTY_ONESHOT_RECONNECT
    private const uint OneShotDisconnect = 1; // KSPROPERTY_ONESHOT_DISCONNECT
    private const uint ClsCtxAll = 0x17;
    private static readonly Guid IidDeviceTopology = typeof(IDeviceTopology).GUID;
    private static readonly Guid IidKsControl = typeof(IKsControl).GUID;

    public string Name => "Audio driver one-shot reconnect (KSPROPSETID_BtAudio)";

    /// <summary>Details of the last call, for the diagnostics log.</summary>
    public string LastDiagnostics { get; private set; } = "";

    /// <summary>
    /// Read-only: state of each render endpoint (Headphones / Headset) in the device's container.
    /// DEVICE_STATE_*: 1 = active, 2 = disabled, 4 = not present, 8 = unplugged (mmdeviceapi.h).
    /// An audio-profile disconnect shows up here even while the Bluetooth link itself stays up.
    /// </summary>
    public IReadOnlyList<(string Name, uint State)> GetEndpointStates()
    {
        var result = new List<(string, uint)>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            if (enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceState.All, out var collection) < 0)
            {
                return result;
            }

            collection.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var endpoint) < 0)
                {
                    continue;
                }

                try
                {
                    if (ContainerOf(endpoint) == containerId && endpoint.GetState(out var state) >= 0)
                    {
                        result.Add((FriendlyNameOf(endpoint) ?? "endpoint", state));
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(endpoint);
                }
            }

            Marshal.ReleaseComObject(collection);
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }

        return result;
    }

    private static string? FriendlyNameOf(IMMDevice endpoint)
    {
        if (endpoint.OpenPropertyStore(0, out var store) < 0)
        {
            return null;
        }

        try
        {
            var key = PropertyKey.DeviceFriendlyName;
            if (store.GetValue(ref key, out var value) < 0)
            {
                return null;
            }

            var name = value.AsString();
            PropVariantNative.PropVariantClear(ref value);
            return name;
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    /// <summary>Read-only diagnostic: how many Bluetooth audio KS filters belong to the device (sends nothing).</summary>
    public int CountFilters() => FindBluetoothFilters([]).Count;

    public Task<bool> RequestConnectAsync(CancellationToken ct) => SendAsync(OneShotReconnect, ct);

    public Task<bool> RequestDisconnectAsync(CancellationToken ct) => SendAsync(OneShotDisconnect, ct);

    /// <summary>
    /// The KS call can block for seconds while Windows pages the device (AirPodsDesktop QuickConnect.cpp L77),
    /// so it runs on a dedicated background (MTA) thread. Filter IDs contain the device address, so only a
    /// hash of each is logged.
    /// </summary>
    private Task<bool> SendAsync(uint property, CancellationToken ct) =>
        Task.Factory.StartNew(() => Send(property), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private bool Send(uint property)
    {
        var log = new List<string>();
        var anyOk = false;
        foreach (var filterId in FindBluetoothFilters(log))
        {
            var hr = SendToFilter(filterId, property);
            log.Add($"KsProperty({(property == OneShotReconnect ? "RECONNECT" : "DISCONNECT")}) on filter #{filterId.GetHashCode():X8}: hr=0x{hr:X8}");
            anyOk |= hr >= 0;
        }

        if (log.Count == 0)
        {
            log.Add("No Bluetooth audio KS filter found for this device's container.");
        }

        LastDiagnostics = string.Join(Environment.NewLine, log);
        return anyOk;
    }

    /// <summary>
    /// Render endpoints in the device's container → their topology connectors → the KS filter on the other side.
    /// Endpoints of disconnected devices still exist (DEVICE_STATEMASK_ALL), which is what makes reconnect possible.
    /// </summary>
    private List<string> FindBluetoothFilters(List<string> log)
    {
        var filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            if (enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceState.All, out var collection) < 0)
            {
                return [];
            }

            collection.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var endpoint) < 0)
                {
                    continue;
                }

                try
                {
                    if (ContainerOf(endpoint) != containerId)
                    {
                        continue;
                    }

                    var iid = IidDeviceTopology;
                    if (endpoint.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var topoObj) < 0)
                    {
                        continue;
                    }

                    var topology = (IDeviceTopology)topoObj;
                    topology.GetConnectorCount(out var connectors);
                    for (uint c = 0; c < connectors; c++)
                    {
                        if (topology.GetConnector(c, out var connector) >= 0
                            && connector.GetDeviceIdConnectedTo(out var filterId) >= 0
                            && !string.IsNullOrEmpty(filterId))
                        {
                            filters.Add(filterId);
                        }
                    }

                    Marshal.ReleaseComObject(topoObj);
                }
                finally
                {
                    Marshal.ReleaseComObject(endpoint);
                }
            }

            Marshal.ReleaseComObject(collection);
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }

        log.Add($"Found {filters.Count} KS filter(s) for the device.");
        return [.. filters];
    }

    private static int SendToFilter(string filterId, uint property)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        try
        {
            var hr = enumerator.GetDevice(filterId, out var filterDevice);
            if (hr < 0)
            {
                return hr;
            }

            try
            {
                var iid = IidKsControl;
                hr = filterDevice.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var ksObj);
                if (hr < 0)
                {
                    return hr;
                }

                var ks = (IKsControl)ksObj;
                var prop = new KsProperty(KsPropSetIdBtAudio, property, KsProperty.TypeGet);
                hr = ks.KsProperty(ref prop, (uint)Marshal.SizeOf<KsProperty>(), IntPtr.Zero, 0, out _);
                Marshal.ReleaseComObject(ksObj);
                return hr;
            }
            finally
            {
                Marshal.ReleaseComObject(filterDevice);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static Guid? ContainerOf(IMMDevice endpoint)
    {
        if (endpoint.OpenPropertyStore(0 /* STGM_READ */, out var store) < 0)
        {
            return null;
        }

        try
        {
            var key = PropertyKey.DeviceContainerId;
            if (store.GetValue(ref key, out var value) < 0)
            {
                return null;
            }

            var result = value.AsGuid();
            PropVariantNative.PropVariantClear(ref value);
            return result;
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }
}
