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
            _ = LoadSpatialAsync();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_service is not null)
        {
            _service.Changed -= OnServiceChanged;
        }
    }

    private void OnServiceChanged(object? sender, EventArgs e) => Render();

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
        if (s.Current is not null)
        {
            Presence.Text = "Nearby and advertising.";
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
            BatteryText.Text = ChargingText.Text = WornText.Text = "Unknown";
            SignalText.Text = "-";
        }
        else
        {
            var st = shown.Status;
            BatteryText.Text = st.Form == Core.Parsing.DeviceForm.Earbuds
                ? $"L {Display.Battery(st.LeftBattery, show)} · R {Display.Battery(st.RightBattery, show)} · Case {Display.Battery(st.CaseBattery, show)}"
                : Display.Battery(st.Battery, show);
            ChargingText.Text = Display.Flag(st.Form == Core.Parsing.DeviceForm.Earbuds ? st.CaseCharging : st.Charging, show, "Yes", "No");
            WornText.Text = Display.Flag(st.AnyWorn, show, "On head", "Off head");
            SignalText.Text = s.Current is null ? "-" : Display.Signal(shown);
        }

        AutoPauseText.Text = !s.Settings.AutoPause
            ? "Off (Settings)"
            : shown?.Status.AnyWorn is { Confidence: Core.Parsing.Confidence.VerifiedOnDevice }
                ? "Active: pauses when you take the headphones off, resumes when you put them back on."
                : "Waiting: needs a verified on-head reading for this model.";
    }

    private void PopulateDevices()
    {
        _updatingPickers = true;
        DevicePicker.ItemsSource = _service!.PairedAppleDevices;
        DevicePicker.SelectedItem = _service.PairedAppleDevices.FirstOrDefault(d => d.DeviceId == _service.Settings.BoundDeviceId);
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
        if (_updatingPickers || DevicePicker.SelectedItem is not PairedDevice device)
        {
            return;
        }

        _service!.Bind(device);
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
            SpatialStatus.Text = $"Output: {state.EndpointName}";
        }
        catch (Exception ex)
        {
            SpatialStatus.Text = $"Could not read spatial sound: {ex.Message}";
        }
    }

    private async void OnSpatialPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingPickers || _spatialEndpointId is null || SpatialPicker.SelectedItem is not string name)
        {
            return;
        }

        if (name == SpatialOff)
        {
            // The public API sets a format but documents no "off" value; don't guess one.
            SpatialStatus.Text = "To turn spatial sound off, use Windows sound settings (button on the right).";
            return;
        }

        var format = ((IEnumerable<SpatialFormat>)SpatialPicker.Tag).First(f => f.Name == name);
        try
        {
            var status = await SpatialSoundController.SetAsync(_spatialEndpointId, format.Subtype);
            SpatialStatus.Text = status == "Succeeded" ? $"{name} is on." : $"Windows did not apply {name}: {status}.";
        }
        catch (Exception ex)
        {
            SpatialStatus.Text = $"Could not set spatial sound: {ex.Message}";
        }

        await LoadSpatialAsync();
    }

    private async void OnOpenSoundSettings(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:sound"));
}
