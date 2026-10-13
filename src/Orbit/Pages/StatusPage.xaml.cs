using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Orbit.Platform.Audio;
using Orbit.Platform.Bluetooth;
using Orbit.Services;

namespace Orbit.Pages;

public sealed partial class StatusPage : Page
{
    private const string SpatialOff = "Off";
    private OrbitService? _service;
    private bool _updatingPickers;
    private string? _spatialEndpointId;

    public StatusPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _service = App.Service;
        if (_service is not null)
        {
            _service.Changed += OnServiceChanged;
            PopulateDevices();
            Render();
            RenderKey();
            _ = LoadSpatialAsync();
        }

        if (App.Switch is { } sw)
        {
            sw.Changed += OnServiceChanged;
            RenderConnection();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_service is not null)
        {
            _service.Changed -= OnServiceChanged;
        }

        if (App.Switch is { } sw)
        {
            sw.Changed -= OnServiceChanged;
        }
    }

    private void OnServiceChanged(object? sender, EventArgs e)
    {
        // The binding is resolved asynchronously at start-up (and can change): refresh the picker and spatial sound.
        var key = $"{_service?.Settings.BoundDeviceId}|{_service?.PairedAppleDevices.Count}";
        if (key != _renderedBindingKey)
        {
            PopulateDevices();
            _ = LoadSpatialAsync();
        }

        Render();
        RenderConnection();
        RenderKey();
    }

    private string? _renderedBindingKey;

    private void RenderConnection()
    {
        if (App.Switch is not { } sw)
        {
            return;
        }

        var bt = !sw.IsAvailable ? "Choose your device below."
            : sw.IsConnected ? "Connected to this PC"
            : _service?.ConnectedTo == Core.Switching.ConnectedTo.OtherDevice ? "In use by another device (e.g. your Mac)"
            : "Not connected to this PC";
        ConnectionText.Text = _service?.UsbConnected == true ? "Connected by USB-C cable" + (sw.IsConnected ? " and Bluetooth" : "") : bt;
        var usb = _service?.UsbConnected == true;
        UsbChip.Visibility = usb ? Visibility.Visible : Visibility.Collapsed;

        // On the cable, Bluetooth switching failed in testing (the headphones aren't on Bluetooth), so it's disabled.
        SwitchButton.IsEnabled = !usb && sw.IsAvailable && !sw.IsBusy && !sw.IsConnected;
        ReleaseButton.IsEnabled = !usb && sw.IsAvailable && !sw.IsBusy && sw.IsConnected;
        SwitchProgress.Visibility = sw.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        SwitchResultText.Text = usb
            ? "Bluetooth switching is off while the USB-C cable is connected."
            : sw.IsBusy ? "Working..." : sw.LastResult?.Message ?? "";
    }

    private void RenderKey()
    {
        var state = _service!.KeyState;
        RemoveKeyButton.IsEnabled = _service.HasStoredKey;
        RenderMacCheck();
        KeyStatus.Text = state switch
        {
            Core.Crypto.KeyState.None => "No key: battery is shown in 10% steps.",
            Core.Crypto.KeyState.Checking => _service.KeyProgress is var (seen, ok)
                ? $"Checking key: {ok} of {Core.Crypto.ExactBatteryResolver.ConfirmAfter} matching broadcasts ({seen} examined). The headphones change their broadcast every few seconds to a few minutes."
                : "",
            Core.Crypto.KeyState.Confirmed => _service.ExactBatteryVerified
                ? "Showing exact battery (checked against your Mac)."
                : "Key confirmed: showing exact battery. Compare it with your Mac once to verify.",
            Core.Crypto.KeyState.Rejected => "This key does not decrypt these headphones' broadcasts. Check that you copied MagicAccEncKey (not MagicAccIRK) for this device.",
            _ => "",
        };
    }

    private void RenderMacCheck()
    {
        var exact = (_service!.Current ?? _service.LastKnown)?.Status.ExactBattery;
        var show = _service.KeyState == Core.Crypto.KeyState.Confirmed && !_service.ExactBatteryVerified && exact is { HasValue: true };
        MacCheck.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            MacCheckText.Text = $"Orbit shows {exact!.Value.Value}%. Open the Bluetooth (or Battery) menu on your Mac and compare. If the Mac shows the same number, confirm it here.";
        }
    }

    private void OnExactMatchesMac(object sender, RoutedEventArgs e)
    {
        _service!.MarkExactBatteryVerified();
        RenderKey();
    }

    private void OnSaveKey(object sender, RoutedEventArgs e)
    {
        if (_service!.SetKey(KeyBox.Password))
        {
            KeyBox.Password = "";
        }
        else
        {
            KeyStatus.Text = "That isn't a 16-byte key. Paste the MagicAccEncKey value (32 hex digits, dashes allowed) or the whole keychain entry.";
            return;
        }

        RenderKey();
    }

    private void OnRemoveKey(object sender, RoutedEventArgs e)
    {
        _service!.RemoveKey();
        RenderKey();
    }

    private async void OnSwitchClick(object sender, RoutedEventArgs e) => await App.Switch!.SwitchToThisPcAsync();

    private async void OnReleaseClick(object sender, RoutedEventArgs e) => await App.Switch!.ReleaseAsync();

    private void Render()
    {
        var s = _service!;
        var show = s.Settings.ShowUnverified;

        ScannerInfo.IsOpen = s.ScannerError is not null;
        ScannerInfo.Message = s.ScannerError ?? "";

        DeviceName.Text = s.DisplayName;
        DeviceModel.Text = s.BoundProfile is { } p
            ? $"Model 0x{p.ModelId:X4} from Device ID - {(s.BoundDevice!.Connected ? "connected to this PC" : "not connected to this PC")}"
            : s.PairedAppleDevices.Count == 0 ? "No paired Apple headphones found. Pair them in Windows Settings > Bluetooth." : "Choose your device below.";

        var shown = s.Current ?? s.LastKnown;

        // Stale (last-known) values are dimmed so they don't read as live.
        var stale = s.Current is null;
        WornText.Opacity = ChargingText.Opacity = stale ? 0.45 : 1.0;

        if (s.Current is not null)
        {
            Presence.Text = "Nearby and advertising.";
        }
        else if (s.UsbConnected)
        {
            // Observed on the owner's Max (protocol-findings F9): broadcasts on the cable are intermittent.
            Presence.Text = string.Create(CultureInfo.CurrentCulture,
                $"Connected by USB-C cable. On the cable the headphones only broadcast now and then; values shown are from {s.LastKnown?.LastSeen.LocalDateTime:t}.");
        }
        else if (s.LastKnown is { } last)
        {
            Presence.Text = string.Create(CultureInfo.CurrentCulture,
                $"Not heard since {last.LastSeen.LocalDateTime:t}. The headphones stop advertising when idle, in the Smart Case, or out of range; values below are from then.");
        }
        else
        {
            Presence.Text = "Not heard yet. Pick up the headphones (or take them out of the Smart Case) to wake them.";
        }

        if (shown is null)
        {
            BatteryText.Text = "-";
            Wear.IsWorn = null;
            WornText.Text = "Worn: unknown";
            ChargingText.Text = "Charging: unknown";
            SignalText.Text = "No signal";
        }
        else
        {
            var st = shown.Status;
            BatteryText.Text = st.Form == Core.Parsing.DeviceForm.Earbuds
                ? $"L {Display.Battery(st.LeftBattery, show)} · R {Display.Battery(st.RightBattery, show)} · Case {Display.Battery(st.CaseBattery, show)}"
                : Display.BatteryOf(st, show);
            UnverifiedHint.Visibility = Display.IsBatteryUnverified(st) ? Visibility.Visible : Visibility.Collapsed;

            var charging = Display.Flag(st.Form == Core.Parsing.DeviceForm.Earbuds ? st.CaseCharging : st.Charging, show, "Charging", "Not charging");
            ChargingText.Text = charging == "Unknown" ? "Charging: unknown" : charging;
            var worn = Display.Flag(st.AnyWorn, show, "On head", "Off head");
            WornText.Text = worn == "Unknown" ? "Worn: unknown" : worn;

            // Animate only on fresh, displayable readings; asleep or unknown -> dimmed, lifted headphones.
            Wear.IsWorn = s.Current is not null && Display.IsShown(st.AnyWorn, show) ? st.AnyWorn.Value : null;
            SignalText.Text = s.Current is null ? "Asleep" : $"Signal {Display.Signal(shown)}";
        }

        AutoPauseText.Text = !s.Settings.AutoPause
            ? "Off (Settings)"
            : shown?.Status.AnyWorn is { Confidence: Core.Parsing.Confidence.VerifiedOnDevice }
                ? "Active: pauses when you take the headphones off, resumes when you put them back on."
                : "Waiting: needs a verified on-head reading for this model.";
    }

    private void PopulateDevices()
    {
        // WinUI 3 cannot bind DisplayMemberPath to plain .NET records, so show names and map by index.
        _updatingPickers = true;
        var devices = _service!.PairedAppleDevices;
        DevicePicker.ItemsSource = devices.Select(d => d.Name).ToList();
        DevicePicker.SelectedIndex = devices.ToList().FindIndex(d => d.DeviceId == _service.Settings.BoundDeviceId);
        _renderedBindingKey = $"{_service.Settings.BoundDeviceId}|{devices.Count}";
        _updatingPickers = false;
    }

    private async void OnRefreshDevices(object sender, RoutedEventArgs e)
    {
        await _service!.RefreshBindingAsync();
        PopulateDevices();
        await LoadSpatialAsync();
    }

    private async void OnDevicePicked(object sender, SelectionChangedEventArgs e)
    {
        var index = DevicePicker.SelectedIndex;
        if (_updatingPickers || index < 0 || index >= _service!.PairedAppleDevices.Count)
        {
            return;
        }

        _service.Bind(_service.PairedAppleDevices[index]);
        await LoadSpatialAsync();
    }

    private async Task LoadSpatialAsync()
    {
        _spatialEndpointId = null;
        SpatialPicker.IsEnabled = false;
        if (_service?.BoundDevice?.ContainerId is not { } container)
        {
            SpatialStatus.Text = "Choose your device first.";
            return;
        }

        try
        {
            var states = await SpatialSoundController.GetForContainerAsync(container);

            // Prefer the stereo "Headphones" (A2DP) endpoint over the hands-free one.
            var state = states.FirstOrDefault(s => s.EndpointName.StartsWith("Headphones", StringComparison.OrdinalIgnoreCase)) ?? states.FirstOrDefault();
            if (state is null)
            {
                SpatialStatus.Text = "Connect the headphones to this PC to change their spatial sound.";
                return;
            }

            if (!state.Supported || state.AvailableFormats.Count == 0)
            {
                SpatialStatus.Text = $"{state.EndpointName}: Windows reports no spatial sound formats for this output.";
                return;
            }

            _updatingPickers = true;
            SpatialPicker.ItemsSource = new[] { SpatialOff }.Concat(state.AvailableFormats.Select(f => f.Name)).ToList();
            SpatialPicker.SelectedItem = SpatialSoundController.NameOf(state.ActiveSubtype);
            _updatingPickers = false;
            _spatialEndpointId = state.EndpointId;
            SpatialPicker.Tag = state.AvailableFormats;
            SpatialPicker.IsEnabled = true;
            SpatialStatus.Text = $"{SpatialSoundController.NameOf(state.ActiveSubtype)} · {CleanEndpointName(state.EndpointName)}";

            // Dolby Atmos for Headphones only shows up once Dolby's paid Dolby Access app is installed.
            var hasDolby = state.AvailableFormats.Any(f => f.Subtype == Windows.Media.Audio.SpatialAudioFormatSubtype.DolbyAtmosForHeadphones);
            DolbyOffer.Visibility = hasDolby ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            SpatialStatus.Text = $"Could not read spatial sound: {ex.Message}";
        }
    }

    /// <summary>"Headphones (Name  - Find My)" -> "Headphones (Name)".</summary>
    private static string CleanEndpointName(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name.Replace(" - Find My", "", StringComparison.OrdinalIgnoreCase), @"\s{2,}", " ").Replace(" )", ")");

    private async void OnSpatialPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingPickers || _spatialEndpointId is null || SpatialPicker.SelectedItem is not string name)
        {
            return;
        }

        // "Off" = the all-zero format GUID Windows itself reports when nothing is active; setting it was verified on
        // the owner's Max (Off -> Windows Sonic -> Off, read back each time, 2026-10-14).
        var subtype = name == SpatialOff
            ? SpatialSoundController.OffSubtype
            : ((IEnumerable<SpatialFormat>)SpatialPicker.Tag).First(f => f.Name == name).Subtype;
        try
        {
            var status = await SpatialSoundController.SetAsync(_spatialEndpointId, subtype);
            SpatialStatus.Text = status != "Succeeded" ? $"Windows did not apply {name}: {status}."
                : name == SpatialOff ? "Spatial sound is off." : $"{name} is on.";
        }
        catch (Exception ex)
        {
            SpatialStatus.Text = $"Could not set spatial sound: {ex.Message}";
        }

        await LoadSpatialAsync();
    }

    private async void OnOpenSoundSettings(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:sound"));

    // Store search rather than a hard-coded product ID, so the link keeps working if the listing changes.
    private async void OnGetDolby(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-windows-store://search/?query=Dolby%20Access"));
}
