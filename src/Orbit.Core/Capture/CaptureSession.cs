using System.Text;

namespace Orbit.Core.Capture;

/// <summary>
/// Writes advertisements and user tags to a CSV file. Thread-safe: advertisements arrive on
/// WinRT callback threads while tags come from the UI thread.
/// </summary>
public sealed class CaptureSession : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private string _currentTag = "untagged";
    private bool _disposed;

    public CaptureSession(string filePath, bool appleOnly = true)
    {
        FilePath = filePath;
        AppleOnly = appleOnly;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        _writer = new StreamWriter(filePath, append: false, new UTF8Encoding(false));
        _writer.WriteLine(CaptureCsv.Header);
        _writer.Flush();
    }

    public string FilePath { get; }

    public bool AppleOnly { get; }

    public int AdvertisementCount { get; private set; }

    public int ProximityPairingCount { get; private set; }

    public string CurrentTag
    {
        get { lock (_gate) { return _currentTag; } }
    }

    /// <summary>Creates orbit-capture-yyyyMMdd-HHmmss.csv under <paramref name="directory"/>.</summary>
    public static CaptureSession CreateIn(string directory, DateTimeOffset now) =>
        new(Path.Combine(directory, $"orbit-capture-{now.LocalDateTime:yyyyMMdd-HHmmss}.csv"));

    /// <summary>Records a marker row and makes <paramref name="tag"/> the label for following advertisements.</summary>
    public void Tag(string tag, DateTimeOffset timestampUtc, string note = "")
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _currentTag = tag;
            _writer.WriteLine(CaptureCsv.FormatRow(AdvertisementRecord.Marker(timestampUtc, tag, note)));
            _writer.Flush();
        }
    }

    /// <summary>Records an advertisement. Returns false if it was filtered out or the session is closed.</summary>
    public bool Record(DateTimeOffset timestampUtc, ulong address, short rssi, string advType, ushort companyId, byte[] payload)
    {
        if (AppleOnly && companyId != AdvertisementRecord.AppleCompanyId)
        {
            return false;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            var record = new AdvertisementRecord(RecordKind.Advertisement, timestampUtc, address, rssi, advType, companyId, payload, _currentTag, "");
            _writer.WriteLine(CaptureCsv.FormatRow(record));
            AdvertisementCount++;
            if (record.AppleMessageType == 0x07)
            {
                ProximityPairingCount++;
            }

            // Flush periodically so a crash loses little data.
            if (AdvertisementCount % 20 == 0)
            {
                _writer.Flush();
            }

            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Flush();
            _writer.Dispose();
        }
    }
}
