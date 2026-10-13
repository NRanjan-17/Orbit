using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Orbit.Platform;

namespace Orbit.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _loading;

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
        LowBatteryToggle.IsOn = s.LowBatteryToast;
        LowBatteryBox.Value = s.LowBatteryPercent;
        StartupToggle.IsOn = StartupRegistration.IsEnabled();
        UnverifiedToggle.IsOn = s.ShowUnverified;
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
        s.LowBatteryToast = LowBatteryToggle.IsOn;
        if (!double.IsNaN(LowBatteryBox.Value))
        {
            s.LowBatteryPercent = (int)LowBatteryBox.Value;
        }

        s.ShowUnverified = UnverifiedToggle.IsOn;
        RssiValue.Text = $"{s.RssiThreshold} dBm";

        if (s.StartWithWindows != StartupToggle.IsOn)
        {
            s.StartWithWindows = StartupToggle.IsOn;
            StartupRegistration.Set(s.StartWithWindows, Environment.ProcessPath!);
        }

        service.SaveSettings();
        SavedText.Text = "Saved.";
    }
}
