using Orbit.Core;
using Orbit.Core.Analysis;
using Orbit.Core.Capture;
using Orbit.Platform.Bluetooth;

namespace Orbit.Cli;

internal static class Program
{
    private static readonly (char Key, string Tag)[] Tags =
    [
        ('1', CaptureTags.OnHead),
        ('2', CaptureTags.OffHead),
        ('3', CaptureTags.Charging),
        ('4', CaptureTags.NotCharging),
        ('5', CaptureTags.AncOn),
        ('6', CaptureTags.Transparency),
        ('7', CaptureTags.NoiseOff),
        ('8', CaptureTags.ConnectedToMac),
        ('9', CaptureTags.ConnectedToPc),
        ('0', CaptureTags.Disconnected),
        ('c', CaptureTags.InCase),
    ];

    private static int Main(string[] args)
    {
        // Output is meant to be pasted/parsed; avoid locale-specific time separators.
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        var command = args.FirstOrDefault()?.ToLowerInvariant();
        try
        {
            return command switch
            {
                "capture" => Capture(args.Skip(1).ToArray()),
                "analyze" or "analyse" => Analyze(args.Skip(1).ToArray()),
                "devices" => Devices(),
                "spatial" => args.Length > 2 && args[1] == "--set" ? SpatialSet(args[2]) : Spatial(),
                "outputs" => Outputs(),
                "diagnose" => Diagnose(),
                "rfcomm" => Rfcomm(),
                "connect" => Switch(args.Skip(1).ToArray(), connect: true),
                "disconnect" => Switch(args.Skip(1).ToArray(), connect: false),
                _ => Usage(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Usage()
    {
        Console.WriteLine("""
            orbit-cli capture [--out <dir>] [--all-apple] [--seconds <n>]
                Logs every Apple BLE advertisement to <dir>\orbit-capture-*.csv (default: .\captures).
                Press the keys shown on screen to tag the current state. q = quit.
                Without --all-apple only Proximity Pairing (type 0x07) frames are printed (all are saved).
                --seconds stops automatically (also used when input is redirected, so no tag keys).

            orbit-cli analyze <file.csv>... [--group <prefixHex>]
                Groups Proximity Pairing frames by device and lists bytes/bits that differ between tags.

            orbit-cli connect|disconnect [--method hybrid|ks|service] [--watch <seconds>]
                Switches the headphones to/from this PC (changes real state). Default method: hybrid.

            orbit-cli diagnose
                Read-only check of the switching plumbing.

            orbit-cli devices
                Lists paired Bluetooth devices with their Device ID (vendor/product). Addresses are not printed.

            orbit-cli rfcomm
                Read-only list of the paired Apple devices' RFCOMM services.

            orbit-cli spatial [--set sonic|dolby|off]
                Shows (or sets) Windows spatial sound for the headphones' output.

            orbit-cli outputs
                Read-only list of audio outputs: defaults per role, the headphones' endpoints, and the speakers
                auto-output switches to when the headphones are taken off.

            Full guide: docs/cli.md
            """);
        return 2;
    }

    private static int Capture(string[] args)
    {
        var outDir = Option(args, "--out") ?? Path.Combine(Environment.CurrentDirectory, "captures");
        var printAll = args.Contains("--all-apple");
        var seconds = Option(args, "--seconds") is { } s ? int.Parse(s, System.Globalization.CultureInfo.InvariantCulture) : (int?)null;
        var deadline = seconds is null ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(seconds.Value);
        var interactive = !Console.IsInputRedirected;

        using var session = CaptureSession.CreateIn(outDir, DateTimeOffset.Now);
        using var scanner = new AppleAdvertisementScanner();
        var stopped = false;

        scanner.AdvertisementReceived += (_, a) =>
        {
            session.Record(a.TimestampUtc, a.Address, a.Rssi, a.AdvertisementType, AdvertisementRecord.AppleCompanyId, a.Payload);
            if (printAll || (a.Payload.Length > 0 && a.Payload[0] == 0x07))
            {
                Console.WriteLine($"{a.TimestampUtc.LocalDateTime:HH:mm:ss.fff} {AdvertisementRecord.FormatAddress(a.Address)} {a.Rssi,4} dBm  {Hex.Format(a.Payload)}");
            }
        };
        scanner.Stopped += (_, error) =>
        {
            stopped = true;
            Console.Error.WriteLine($"Scanner stopped: {error}. Is Bluetooth on?");
        };

        Console.WriteLine($"Writing {session.FilePath}");
        Console.WriteLine("Tag keys: " + string.Join("  ", Tags.Select(t => $"[{t.Key}] {t.Tag}")) + "  [n] note  [q] quit");
        scanner.Start();
        session.Tag("start", DateTimeOffset.UtcNow);

        while (!stopped && DateTime.UtcNow < deadline)
        {
            if (!interactive || !Console.KeyAvailable)
            {
                Thread.Sleep(50);
                continue;
            }

            var key = char.ToLowerInvariant(Console.ReadKey(intercept: true).KeyChar);
            if (key == 'q')
            {
                break;
            }

            if (key == 'n')
            {
                Console.Write("note> ");
                var note = Console.ReadLine() ?? "";
                session.Tag(session.CurrentTag, DateTimeOffset.UtcNow, note);
                continue;
            }

            var match = Tags.FirstOrDefault(t => t.Key == key);
            if (match.Tag is not null)
            {
                session.Tag(match.Tag, DateTimeOffset.UtcNow);
                Console.WriteLine($"==== TAG: {match.Tag} ====");
            }
        }

        scanner.Stop();
        Console.WriteLine($"Saved {session.AdvertisementCount} Apple advertisements ({session.ProximityPairingCount} proximity pairing) to {session.FilePath}");
        return stopped ? 1 : 0;
    }

    private static int Devices()
    {
        foreach (var d in PairedDeviceCatalog.GetPairedAsync().GetAwaiter().GetResult())
        {
            Console.WriteLine($"{d.Name,-40} connected={d.Connected,-5} vendor={Fmt(d.VendorId)} product={Fmt(d.ProductId)} source={d.VendorIdSource?.ToString() ?? "-"}");
        }

        return 0;

        static string Fmt(ushort? v) => v is { } x ? $"0x{x:X4}" : "-";
    }

    /// <summary>
    /// Read-only: asks each paired Apple device for its RFCOMM services (uncached SDP query). Used to check claims
    /// that Apple's accessory protocol is reachable through WinRT RFCOMM; community sources put it on L2CAP PSM 0x1001.
    /// </summary>
    private static int Rfcomm()
    {
        foreach (var d in PairedDeviceCatalog.GetPairedAsync().GetAwaiter().GetResult().Where(d => d.IsApple))
        {
            using var device = Windows.Devices.Bluetooth.BluetoothDevice.FromIdAsync(d.DeviceId).AsTask().GetAwaiter().GetResult();
            var result = device.GetRfcommServicesAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached).AsTask().GetAwaiter().GetResult();
            Console.WriteLine($"{d.Name}: query {result.Error}, {result.Services.Count} RFCOMM service(s)");
            // ServiceId only: ConnectionServiceName embeds the local and remote Bluetooth addresses.
            foreach (var s in result.Services)
            {
                Console.WriteLine($"  {s.ServiceId.Uuid}");
            }
        }

        return 0;
    }

    /// <summary>Read-only: checks the connect/disconnect plumbing for each paired Apple device without sending anything.</summary>
    private static int Diagnose()
    {
        var size = ServiceStateConnector.DeviceInfoSize;
        Console.WriteLine($"BLUETOOTH_DEVICE_INFO size: {size} (expected 560 on 64-bit)");
        foreach (var d in PairedDeviceCatalog.GetPairedAsync().GetAwaiter().GetResult().Where(d => d.IsApple))
        {
            var filters = d.ContainerId is { } c ? new KsAudioConnector(c).CountFilters() : 0;
            Console.WriteLine($"{d.Name}: connected={d.Connected} bluetoothAudioKsFilters={filters}");
        }

        return 0;
    }

    /// <summary>
    /// Connects/disconnects the first paired Apple device via SwitchCoordinator, printing each step.
    /// Usage: orbit-cli connect|disconnect [--method hybrid|ks|service] [--watch seconds]. Changes real state: run only when asked to.
    /// </summary>
    private static int Switch(string[] args, bool connect)
    {
        var device = PairedDeviceCatalog.GetPairedAsync().GetAwaiter().GetResult().FirstOrDefault(d => d.IsApple && d.ContainerId is not null);
        if (device is null)
        {
            Console.Error.WriteLine("No paired Apple audio device found.");
            return 1;
        }

        using var monitor = ConnectionMonitor.CreateAsync(device.DeviceId).GetAwaiter().GetResult()!;
        Orbit.Core.Switching.IAudioConnector connector = Option(args, "--method") switch
        {
            "service" => new ServiceStateConnector(device.Address, device.Name),
            "ks" => new KsAudioConnector(device.ContainerId!.Value),
            _ => new HybridConnector(device.ContainerId!.Value, device.Address, device.Name),
        };
        var audio = new AudioConnectionState(device.ContainerId!.Value);
        var coordinator = new Orbit.Core.Switching.SwitchCoordinator(connector, audio);

        Console.WriteLine($"{(connect ? "Connecting" : "Releasing")} {device.Name} using {connector.Name}; audio connected now={audio.IsConnected}, link={monitor.IsConnected}");
        var started = DateTime.UtcNow;
        var result = (connect ? coordinator.ConnectAsync() : coordinator.ReleaseAsync()).GetAwaiter().GetResult();
        Console.WriteLine($"Result: {result.Outcome} after {result.Attempts} attempt(s), {(DateTime.UtcNow - started).TotalSeconds:0.0} s: {result.Message}");
        Console.WriteLine(connector switch
        {
            KsAudioConnector k => k.LastDiagnostics,
            ServiceStateConnector s => s.LastDiagnostics,
            HybridConnector h => h.LastDiagnostics,
            _ => "",
        });

        // --watch <seconds>: print Bluetooth link + audio endpoint state whenever it changes.
        if (Option(args, "--watch") is { } w && int.TryParse(w, out var watchSeconds))
        {
            var probe = new KsAudioConnector(device.ContainerId!.Value);
            var last = "";
            var until = DateTime.UtcNow.AddSeconds(watchSeconds);
            while (DateTime.UtcNow < until)
            {
                var endpoints = string.Join(", ", probe.GetEndpointStates().Select(e => $"{e.Name}={StateName(e.State)}"));
                var line = $"link={(monitor.IsConnected ? "connected" : "disconnected")}  {endpoints}";
                if (line != last)
                {
                    Console.WriteLine($"+{(DateTime.UtcNow - started).TotalSeconds,5:0.0}s  {line}");
                    last = line;
                }

                Thread.Sleep(500);
            }
        }
        return result.Outcome is Orbit.Core.Switching.SwitchOutcome.Connected or Orbit.Core.Switching.SwitchOutcome.Released
            or Orbit.Core.Switching.SwitchOutcome.AlreadyConnected or Orbit.Core.Switching.SwitchOutcome.AlreadyReleased ? 0 : 1;
    }

    /// <summary>
    /// Sets the spatial format of the paired Apple headphones' "Headphones" output and reads it back.
    /// value: sonic | dolby | off (off = the all-zero GUID Windows reports when no format is active).
    /// </summary>
    private static int SpatialSet(string value)
    {
        var subtype = value switch
        {
            "sonic" => Windows.Media.Audio.SpatialAudioFormatSubtype.WindowsSonic,
            "dolby" => Windows.Media.Audio.SpatialAudioFormatSubtype.DolbyAtmosForHeadphones,
            "off" => Orbit.Platform.Audio.SpatialSoundController.OffSubtype,
            _ => throw new ArgumentException("use sonic, dolby or off"),
        };

        var device = PairedDeviceCatalog.GetPairedAsync().GetAwaiter().GetResult().First(d => d.IsApple && d.ContainerId is not null);
        var states = Orbit.Platform.Audio.SpatialSoundController.GetForContainerAsync(device.ContainerId!.Value).GetAwaiter().GetResult();
        var endpoint = states.FirstOrDefault(s => s.EndpointName.StartsWith("Headphones", StringComparison.OrdinalIgnoreCase)) ?? states.First();
        Console.WriteLine($"{endpoint.EndpointName}: before = {Orbit.Platform.Audio.SpatialSoundController.NameOf(endpoint.ActiveSubtype)}");
        var status = Orbit.Platform.Audio.SpatialSoundController.SetAsync(endpoint.EndpointId, subtype).GetAwaiter().GetResult();
        Thread.Sleep(500);
        Console.WriteLine($"set {value}: {status}; after = {Orbit.Platform.Audio.SpatialSoundController.NameOf(Orbit.Platform.Audio.SpatialSoundController.GetActive(endpoint.EndpointId))}");
        return 0;
    }

    private static string StateName(uint state) => state switch
    {
        1 => "active",
        2 => "disabled",
        4 => "notpresent",
        8 => "unplugged",
        _ => state.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>Read-only: prints Windows spatial sound state for every paired Apple device's audio endpoints.</summary>
    private static int Spatial()
    {
        foreach (var d in PairedDeviceCatalog.GetPairedAsync().GetAwaiter().GetResult().Where(d => d.IsApple && d.ContainerId is not null))
        {
            Console.WriteLine(d.Name);
            var states = Orbit.Platform.Audio.SpatialSoundController.GetForContainerAsync(d.ContainerId!.Value).GetAwaiter().GetResult();
            if (states.Count == 0)
            {
                Console.WriteLine("  (no active render endpoint; connect the device to this PC)");
            }

            foreach (var s in states)
            {
                Console.WriteLine($"  {s.EndpointName}: supported={s.Supported} active={Orbit.Platform.Audio.SpatialSoundController.NameOf(s.ActiveSubtype)} available=[{string.Join(", ", s.AvailableFormats.Select(f => f.Name))}]");
            }
        }

        return 0;
    }

    private static int Analyze(string[] args)
    {
        var group = Option(args, "--group");
        var files = args.Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal) && (i == 0 || args[i - 1] != "--group")).ToList();
        if (files.Count == 0)
        {
            return Usage();
        }

        var records = files.SelectMany(CaptureCsv.ReadFile).ToList();
        Console.WriteLine(CaptureAnalyzer.FormatReport(CaptureAnalyzer.Analyze(records, groupPrefixHex: group)));
        return 0;
    }

    private static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int Outputs()
    {
        var audio = new Orbit.Platform.Audio.WindowsAudioOutputs();
        var outputs = audio.GetOutputs();
        var defaults = Core.Media.AudioOutputs.AllRoles.Select(r => (Role: r, Id: audio.GetDefault(r))).ToList();
        var headphones = PairedDeviceCatalog.GetPairedAsync().GetAwaiter().GetResult()
            .FirstOrDefault(d => d.IsApple && d.ContainerId is not null)?.ContainerId;
        var speakers = Core.Media.AutoOutputController.PickSpeakers(outputs, headphones);
        foreach (var o in outputs)
        {
            var roles = defaults.Where(d => string.Equals(d.Id, o.Id, StringComparison.OrdinalIgnoreCase)).Select(d => d.Role.ToString().ToLowerInvariant());
            var marks = new List<string>();
            if (roles.Any())
            {
                marks.Add("default: " + string.Join(", ", roles));
            }

            if (headphones is not null && o.ContainerId == headphones)
            {
                marks.Add("headphones");
            }

            if (o.Id == speakers?.Id)
            {
                marks.Add("auto-output speakers");
            }

            Console.WriteLine($"{o.Kind,-10} {o.Name}{(marks.Count > 0 ? "  [" + string.Join("; ", marks) + "]" : "")}");
        }

        if (speakers is null)
        {
            Console.WriteLine("No speakers found: auto-output would leave the output unchanged.");
        }

        return 0;
    }
}


