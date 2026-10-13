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

            orbit-cli devices
                Lists paired Bluetooth devices with their Device ID (vendor/product). Addresses are not printed.
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
}


