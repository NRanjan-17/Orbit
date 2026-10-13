using System.Globalization;
using System.Text;

namespace Orbit.Core.Capture;

/// <summary>
/// CSV format for advertisement captures. One row per advertisement or marker.
/// Columns: kind,timestamp_utc,address,rssi,adv_type,company_id,msg_type,payload_len,payload_hex,tag,note
/// </summary>
public static class CaptureCsv
{
    public const string Header = "kind,timestamp_utc,address,rssi,adv_type,company_id,msg_type,payload_len,payload_hex,tag,note";

    public static string FormatRow(AdvertisementRecord r)
    {
        var isMarker = r.Kind == RecordKind.Marker;
        var fields = new[]
        {
            isMarker ? "MARKER" : "ADV",
            r.TimestampUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            isMarker ? "" : r.AddressText,
            isMarker ? "" : r.Rssi.ToString(CultureInfo.InvariantCulture),
            r.AdvertisementType,
            isMarker ? "" : "0x" + r.CompanyId.ToString("X4"),
            r.AppleMessageType is { } t ? "0x" + t.ToString("X2") : "",
            isMarker ? "" : r.Payload.Length.ToString(CultureInfo.InvariantCulture),
            r.PayloadHex,
            r.Tag,
            r.Note,
        };
        return string.Join(',', fields.Select(Escape));
    }

    public static IEnumerable<AdvertisementRecord> Read(TextReader reader)
    {
        var header = reader.ReadLine();
        if (header is null)
        {
            yield break;
        }

        if (!header.StartsWith("kind,", StringComparison.Ordinal))
        {
            throw new FormatException("Not an Orbit capture file (missing header).");
        }

        string? line;
        var lineNo = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNo++;
            if (line.Length == 0)
            {
                continue;
            }

            var f = SplitCsvLine(line);
            if (f.Count < 11)
            {
                throw new FormatException($"Line {lineNo}: expected 11 fields, got {f.Count}.");
            }

            var kind = f[0] == "MARKER" ? RecordKind.Marker : RecordKind.Advertisement;
            var ts = DateTimeOffset.Parse(f[1], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
            yield return new AdvertisementRecord(
                kind,
                ts,
                ParseAddress(f[2]),
                f[3].Length == 0 ? (short)0 : short.Parse(f[3], CultureInfo.InvariantCulture),
                f[4],
                f[5].Length == 0 ? (ushort)0 : Convert.ToUInt16(f[5], 16),
                f[8].Length == 0 ? [] : Hex.Parse(f[8]),
                f[9],
                f[10]);
        }
    }

    public static IReadOnlyList<AdvertisementRecord> ReadFile(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8);
        return Read(reader).ToList();
    }

    private static ulong ParseAddress(string text) =>
        text.Length == 0 ? 0 : Convert.ToUInt64(text.Replace(":", "", StringComparison.Ordinal), 16);

    private static string Escape(string value)
    {
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    internal static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        result.Add(sb.ToString());
        return result;
    }
}
