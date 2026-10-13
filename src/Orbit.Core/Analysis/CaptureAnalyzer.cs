using System.Globalization;
using System.Text;
using Orbit.Core.Capture;

namespace Orbit.Core.Analysis;

/// <summary>
/// Layout-agnostic analysis of tagged captures. It does not assume any field positions:
/// it groups Proximity Pairing frames by their leading bytes, then reports which byte
/// offsets / nibbles / bits change consistently between user-tagged states.
/// The output is evidence for a human to confirm, not an automatic decoder.
/// </summary>
public static class CaptureAnalyzer
{
    /// <summary>Number of leading payload bytes used to group frames into "devices".
    /// 5 = message type, length, and the next 3 bytes, which for every frame seen so far
    /// stay constant per device. Grouping is a heuristic; the report prints the groups.</summary>
    public const int GroupPrefixLength = 5;

    public static AnalysisReport Analyze(IEnumerable<AdvertisementRecord> records, byte messageType = 0x07, string? groupPrefixHex = null)
    {
        var frames = records
            .Where(r => r.Kind == RecordKind.Advertisement && r.AppleMessageType == messageType)
            .ToList();

        var groups = frames
            .GroupBy(f => Hex.Format(f.Payload.AsSpan(0, Math.Min(GroupPrefixLength, f.Payload.Length))))
            .Select(g => new DeviceGroup(
                g.Key,
                g.Count(),
                Median(g.Select(r => (int)r.Rssi)),
                g.Max(r => r.Rssi),
                g.Select(r => r.Payload.Length).Distinct().Order().ToArray(),
                g.Select(r => r.Address).Distinct().Count()))
            .OrderByDescending(g => g.MedianRssi)
            .ThenByDescending(g => g.Count)
            .ToList();

        var selectedKey = groupPrefixHex?.ToUpperInvariant() ?? groups.FirstOrDefault()?.PrefixHex;
        var selected = selectedKey is null
            ? []
            : frames.Where(f => Hex.Format(f.Payload.AsSpan(0, Math.Min(GroupPrefixLength, f.Payload.Length))) == selectedKey).ToList();

        var byTag = selected.GroupBy(f => f.Tag).ToDictionary(g => g.Key, g => g.ToList());
        var length = selected.Count == 0 ? 0 : selected.Min(f => f.Payload.Length);

        var offsets = new List<OffsetSummary>();
        for (var i = 0; i < length; i++)
        {
            var all = selected.Select(f => f.Payload[i]).ToList();
            var distinct = all.Distinct().Count();
            var perTag = byTag.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.GroupBy(f => f.Payload[i]).OrderByDescending(g => g.Count())
                    .Select(g => new ValueShare(g.Key, (double)g.Count() / kv.Value.Count)).Take(3).ToList());
            offsets.Add(new OffsetSummary(i, distinct, LooksRandom(all), perTag));
        }

        var bits = new List<BitDifference>();
        var tags = byTag.Keys.Order(StringComparer.Ordinal).ToList();
        for (var a = 0; a < tags.Count; a++)
        {
            for (var b = a + 1; b < tags.Count; b++)
            {
                var fa = byTag[tags[a]];
                var fb = byTag[tags[b]];
                for (var i = 0; i < length; i++)
                {
                    if (offsets[i].LikelyRandom)
                    {
                        continue;
                    }

                    for (var bit = 7; bit >= 0; bit--)
                    {
                        var ra = fa.Count(f => (f.Payload[i] >> bit & 1) == 1) / (double)fa.Count;
                        var rb = fb.Count(f => (f.Payload[i] >> bit & 1) == 1) / (double)fb.Count;
                        if (Math.Abs(ra - rb) >= 0.8)
                        {
                            bits.Add(new BitDifference(tags[a], tags[b], i, bit, ra, rb));
                        }
                    }
                }
            }
        }

        var markers = records.Where(r => r.Kind == RecordKind.Marker).ToList();
        return new AnalysisReport(frames.Count, groups, selectedKey, selected.Count, tags, byTag.ToDictionary(kv => kv.Key, kv => kv.Value.Count), offsets, bits, markers);
    }

    /// <summary>
    /// An offset is "likely random" (encrypted/rotating) when almost every frame carries a
    /// different value. Status/battery bytes repeat many times between changes.
    /// </summary>
    private static bool LooksRandom(IReadOnlyList<byte> values) =>
        values.Count >= 20 && values.Distinct().Count() >= Math.Min(64, values.Count * 0.5);

    private static int Median(IEnumerable<int> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }

    public static string FormatReport(AnalysisReport r)
    {
        var sb = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;
        sb.AppendLine($"Proximity Pairing frames: {r.FrameCount}");
        sb.AppendLine();
        sb.AppendLine("Device groups (by first 5 payload bytes), strongest first:");
        sb.AppendLine("  prefix        frames  medianRSSI  maxRSSI  lengths  addresses");
        foreach (var g in r.Groups)
        {
            sb.AppendLine(string.Create(ci, $"  {g.PrefixHex,-12}  {g.Count,6}  {g.MedianRssi,10}  {g.MaxRssi,7}  {string.Join('/', g.PayloadLengths),7}  {g.DistinctAddresses,9}"));
        }

        sb.AppendLine();
        if (r.SelectedPrefix is null)
        {
            sb.AppendLine("No frames to analyse.");
            return sb.ToString();
        }

        sb.AppendLine($"Selected group {r.SelectedPrefix}: {r.SelectedCount} frames");
        sb.AppendLine("Frames per tag: " + string.Join(", ", r.Tags.Select(t => $"{t}={r.FramesPerTag[t]}")));
        sb.AppendLine("Markers: " + string.Join(" | ", r.Markers.Select(m => $"{m.TimestampUtc.LocalDateTime:HH:mm:ss} {m.Tag}{(m.Note.Length > 0 ? " (" + m.Note + ")" : "")}")));
        sb.AppendLine();
        sb.AppendLine("Per-offset values (top value per tag, share in %). '*' = looks random/encrypted:");
        foreach (var o in r.Offsets)
        {
            sb.Append(string.Create(ci, $"  [{o.Offset,2}] distinct={o.DistinctValues,3}{(o.LikelyRandom ? " *" : "  ")} "));
            if (!o.LikelyRandom)
            {
                sb.Append(string.Join("  ", r.Tags.Select(t =>
                {
                    var top = o.TopValuesPerTag[t];
                    return string.Create(ci, $"{t}:{string.Join('/', top.Select(v => $"{v.Value:X2}({v.Share * 100:0})"))}");
                })));
            }

            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("Bits that separate two tags (>=80% difference in how often the bit is set):");
        if (r.BitDifferences.Count == 0)
        {
            sb.AppendLine("  (none)");
        }

        foreach (var d in r.BitDifferences.OrderBy(d => d.Offset).ThenByDescending(d => d.Bit))
        {
            sb.AppendLine(string.Create(ci, $"  byte[{d.Offset,2}] bit{d.Bit} (mask 0x{1 << d.Bit:X2}): {d.TagA}={d.ShareSetA * 100:0}% set, {d.TagB}={d.ShareSetB * 100:0}% set"));
        }

        return sb.ToString();
    }
}

public sealed record DeviceGroup(string PrefixHex, int Count, int MedianRssi, short MaxRssi, int[] PayloadLengths, int DistinctAddresses);

public sealed record ValueShare(byte Value, double Share);

public sealed record OffsetSummary(int Offset, int DistinctValues, bool LikelyRandom, IReadOnlyDictionary<string, List<ValueShare>> TopValuesPerTag);

public sealed record BitDifference(string TagA, string TagB, int Offset, int Bit, double ShareSetA, double ShareSetB);

public sealed record AnalysisReport(
    int FrameCount,
    IReadOnlyList<DeviceGroup> Groups,
    string? SelectedPrefix,
    int SelectedCount,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, int> FramesPerTag,
    IReadOnlyList<OffsetSummary> Offsets,
    IReadOnlyList<BitDifference> BitDifferences,
    IReadOnlyList<AdvertisementRecord> Markers);
