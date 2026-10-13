using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Orbit.Core;
using Orbit.Core.Capture;
using Orbit.Platform.Bluetooth;

namespace Orbit.Pages;

/// <summary>Debug page: records raw Apple advertisements plus user tags to CSV.</summary>
public sealed partial class CapturePage : Page
{
    private const int MaxVisibleFrames = 300;

    private readonly ObservableCollection<string> _frames = [];
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private AppleAdvertisementScanner? _scanner;
    private CaptureSession? _session;

    public CapturePage()
    {
        InitializeComponent();
        FrameList.ItemsSource = _frames;
        _uiTimer.Tick += (_, _) => FlushPending();

        foreach (var tag in CaptureTags.All)
        {
            var button = new Button { Content = tag, Tag = tag, IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 8, 8) };
            button.Click += OnTagClick;
            TagButtons.Items.Add(button);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        // Keep capturing while the user looks at other pages? No: stop cleanly so the file is complete.
        StopCapture();
        base.OnNavigatedFrom(e);
    }

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _session = CaptureSession.CreateIn(AppPaths.CapturesDirectory, DateTimeOffset.Now);
            _scanner = new AppleAdvertisementScanner();
            _scanner.AdvertisementReceived += OnAdvertisement;
            _scanner.Stopped += OnScannerStopped;
            _scanner.Start();
            _session.Tag("start", DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            StopCapture();
            StatusText.Text = $"Could not start: {ex.Message}. Check that Bluetooth is on.";
            return;
        }

        _frames.Clear();
        SetCapturing(true);
        _uiTimer.Start();
        UpdateStatus();
    }

    private void OnStopClick(object sender, RoutedEventArgs e) => StopCapture();

    private void StopCapture()
    {
        _uiTimer.Stop();
        if (_scanner is not null)
        {
            _scanner.AdvertisementReceived -= OnAdvertisement;
            _scanner.Stopped -= OnScannerStopped;
            _scanner.Dispose();
            _scanner = null;
        }

        if (_session is not null)
        {
            var s = _session;
            _session = null;
            s.Dispose();
            StatusText.Text = $"Saved {s.AdvertisementCount} Apple advertisements ({s.ProximityPairingCount} proximity pairing) to {s.FilePath}";
        }

        SetCapturing(false);
    }

    private void OnAdvertisement(object? sender, RawAppleAdvertisement a)
    {
        var session = _session;
        if (session is null || !session.Record(a.TimestampUtc, a.Address, a.Rssi, a.AdvertisementType, AdvertisementRecord.AppleCompanyId, a.Payload))
        {
            return;
        }

        _pending.Enqueue(string.Create(CultureInfo.InvariantCulture, $"{a.TimestampUtc.LocalDateTime:HH:mm:ss.fff}  {a.Rssi,4} dBm  {AdvertisementRecord.FormatAddress(a.Address)}  {Hex.Format(a.Payload)}"));
    }

    private void OnScannerStopped(object? sender, string error)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StopCapture();
            StatusText.Text = $"Bluetooth scanner stopped ({error}). Is Bluetooth turned on?";
        });
    }

    private void FlushPending()
    {
        var onlyProximity = OnlyProximityCheck.IsChecked == true;
        while (_pending.TryDequeue(out var line))
        {
            // Payload hex follows the address; message type 0x07 => payload starts with "07".
            if (onlyProximity && !IsProximity(line))
            {
                continue;
            }

            _frames.Insert(0, line);
        }

        while (_frames.Count > MaxVisibleFrames)
        {
            _frames.RemoveAt(_frames.Count - 1);
        }

        UpdateStatus();

        bool IsProximity(string l) => l[(l.LastIndexOf(' ') + 1)..].StartsWith("07", StringComparison.Ordinal);
    }

    private void OnTagClick(object sender, RoutedEventArgs e)
    {
        if (_session is null || sender is not Button { Tag: string tag })
        {
            return;
        }

        _session.Tag(tag, DateTimeOffset.UtcNow);
        _frames.Insert(0, string.Create(CultureInfo.InvariantCulture, $"==== TAG: {tag} at {DateTime.Now:HH:mm:ss} ===="));
        UpdateStatus();
    }

    private void OnNoteClick(object sender, RoutedEventArgs e)
    {
        if (_session is null || string.IsNullOrWhiteSpace(NoteBox.Text))
        {
            return;
        }

        _session.Tag(_session.CurrentTag, DateTimeOffset.UtcNow, NoteBox.Text.Trim());
        _frames.Insert(0, $"---- NOTE: {NoteBox.Text.Trim()} ----");
        NoteBox.Text = "";
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.CapturesDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.CapturesDirectory}\"") { UseShellExecute = true });
    }

    private void SetCapturing(bool capturing)
    {
        StartButton.IsEnabled = !capturing;
        StopButton.IsEnabled = capturing;
        NoteButton.IsEnabled = capturing;
        foreach (var item in TagButtons.Items.OfType<Button>())
        {
            item.IsEnabled = capturing;
        }
    }

    private void UpdateStatus()
    {
        if (_session is null)
        {
            return;
        }

        StatusText.Text = $"Capturing to {_session.FilePath}  -  {_session.AdvertisementCount} Apple frames, {_session.ProximityPairingCount} proximity pairing";
        CurrentTagText.Text = $"Current tag: {_session.CurrentTag}";
    }
}

