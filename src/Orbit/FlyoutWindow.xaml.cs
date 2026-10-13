using System.Globalization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Orbit.Services;

namespace Orbit;

/// <summary>Small borderless status window shown above the tray icon. Hides when it loses focus.</summary>
public sealed partial class FlyoutWindow : Window
{
    private const int WidthDip = 340;
    private const int HeightDip = 230;

    private readonly PopupBehavior _popup;

    public FlyoutWindow()
    {
        InitializeComponent();
        PopupBehavior.ConfigureChrome(this);
        _popup = new PopupBehavior(this);

        if (App.Service is { } s)
        {
            s.Changed += (_, _) => Render();
        }
    }

    public void ShowNearTray()
    {
        if (_popup.IsVisible)
        {
            _popup.Hide(); // second click on the tray icon closes it
            return;
        }

        Render();
        var scale = (Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? GetDpiForSystem() / 96.0;
        var w = (int)(WidthDip * scale);
        var h = (int)(HeightDip * scale);
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        _popup.Show(new Windows.Graphics.RectInt32(work.X + work.Width - w - 12, work.Y + work.Height - h - 12, w, h));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private void Render()
    {
        if (App.Service is not { } s)
        {
            return;
        }

        var show = s.Settings.ShowUnverified;
        var shown = s.Current ?? s.LastKnown;
        NameText.Text = s.DisplayName;
        BatteryText.Text = shown is null ? "-" : Display.BatteryOf(shown.Status, show);
        WornText.Text = shown is null ? "" : Display.Flag(shown.Status.AnyWorn, show, "On head", "Off head");
        ChargingText.Text = shown is null ? "" : "Charging: " + Display.Flag(shown.Status.Charging, show, "yes", "no");
        PresenceText.Text = s.Current is not null
            ? "Nearby"
            : shown is null ? "Not heard yet" : string.Create(CultureInfo.CurrentCulture, $"Asleep or out of range (last seen {shown.LastSeen.LocalDateTime:t})");
        AutoPauseButton.IsChecked = s.Settings.AutoPause;
    }

    private void OnAutoPauseClick(object sender, RoutedEventArgs e)
    {
        if (App.Service is { } s)
        {
            s.Settings.AutoPause = AutoPauseButton.IsChecked == true;
            s.SaveSettings();
        }
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        _popup.Hide();
        (Application.Current as App)?.ShowMainWindow();
    }
}
