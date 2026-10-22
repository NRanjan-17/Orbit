using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Orbit.Platform;

namespace Orbit.Pages;

public sealed partial class SettingsPage : Page
{
    // True until OnNavigatedTo has filled every control. Controls raise change events while InitializeComponent is
    // still building the page (e.g. the slider clamps its default 0 into -95..-40), when other fields are null.
    private bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var s = App.Service!.Settings;
        _loading = true;
        RssiSlider.Value = s.RssiThreshold;
        RssiValue.Text = $"{s.RssiThreshold} dBm";
        AutoPauseToggle.IsOn = s.AutoPause;
        AutoResumeToggle.IsOn = s.AutoResume;
        AutoOutputToggle.IsOn = s.AutoSwitchOutput;
        LowBatteryToggle.IsOn = s.LowBatteryToast;
        LowBatteryBox.Value = s.LowBatteryPercent;
        StartupToggle.IsOn = StartupRegistration.IsEnabled();
        UnverifiedToggle.IsOn = s.ShowUnverified;
        TrayBatteryToggle.IsOn = s.TrayShowsBattery;
        EstimateToggle.IsOn = s.EstimateBattery;
        ConnectToastToggle.IsOn = s.ConnectToast;
        ResponseBox.SelectedItem = ResponseBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.AutoPauseResponse) ?? ResponseBox.Items[0];
        ChimeConnectToggle.IsOn = s.ChimeOnConnect;
        ChimePutOnToggle.IsOn = s.ChimeOnPutOn;
        HotkeyBox.Text = s.Hotkey;
        AutoConnectToggle.IsOn = s.AutoConnect;
        MethodBox.SelectedItem = MethodBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.SwitchMethod) ?? MethodBox.Items[0];
        SetNote(HotkeyError, App.Switch?.HotkeyError ?? "");
        _loading = false;
    }

    private void OnNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => Save();

    private void OnChanged(object sender, object e) => Save();

    private void Save()
    {
        if (_loading || App.Service is not { } service)
        {
            return;
        }

        var s = service.Settings;
        s.RssiThreshold = (int)RssiSlider.Value;
        s.AutoPause = AutoPauseToggle.IsOn;
        s.AutoResume = AutoResumeToggle.IsOn;
        s.AutoSwitchOutput = AutoOutputToggle.IsOn;
        s.LowBatteryToast = LowBatteryToggle.IsOn;
        if (!double.IsNaN(LowBatteryBox.Value))
        {
            s.LowBatteryPercent = (int)LowBatteryBox.Value;
        }

        s.ShowUnverified = UnverifiedToggle.IsOn;
        s.TrayShowsBattery = TrayBatteryToggle.IsOn;
        s.EstimateBattery = EstimateToggle.IsOn;
        s.ConnectToast = ConnectToastToggle.IsOn;
        RssiValue.Text = $"{s.RssiThreshold} dBm";

        if (s.StartWithWindows != StartupToggle.IsOn)
        {
            s.StartWithWindows = StartupToggle.IsOn;
            StartupRegistration.Set(s.StartWithWindows, Environment.ProcessPath!);
        }

        s.AutoPauseResponse = (ResponseBox.SelectedItem as ComboBoxItem)?.Tag as string ?? Core.Settings.AutoPauseResponses.Fast;
        s.ChimeOnConnect = ChimeConnectToggle.IsOn;
        s.ChimeOnPutOn = ChimePutOnToggle.IsOn;
        s.Hotkey = HotkeyBox.Text.Trim();
        s.AutoConnect = AutoConnectToggle.IsOn;
        s.SwitchMethod = (MethodBox.SelectedItem as ComboBoxItem)?.Tag as string ?? Core.Settings.SwitchMethods.Hybrid;

        service.SaveSettings();
        _ = SyncSwitchAsync();
        SavedText.Text = "Saved.";
    }

    private async void OnTestChime(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (App.Service?.BoundDevice?.ContainerId is not { } container)
        {
            SetNote(ChimeStatus, "Choose your device on the Status page first.");
            return;
        }

        try
        {
            SetNote(ChimeStatus, await Platform.Audio.ChimePlayer.PlayAsync(container)
                ? "Played on the headphones."
                : "The headphones have no audio output on this PC. Connect them first.");
        }
        catch (Exception ex)
        {
            SetNote(ChimeStatus, $"Could not play: {ex.Message}");
        }
    }

    /// <summary>Shows a row's note line only when it has text, so empty notes don't push the title off-centre.</summary>
    private static void SetNote(TextBlock note, string? text)
    {
        note.Text = text ?? "";
        note.Visibility = string.IsNullOrEmpty(text) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    }

    private async Task SyncSwitchAsync()
    {
        if (App.Switch is { } sw)
        {
            await sw.SyncAsync();
            SetNote(HotkeyError, sw.HotkeyError ?? "");
        }
    }
}
