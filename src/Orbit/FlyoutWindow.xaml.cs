using System.Globalization;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Orbit.Services;

namespace Orbit;

/// <summary>Compact status window shown above the tray icon (left click). Hides when it loses focus.</summary>
public sealed partial class FlyoutWindow : Window
{
    private const double WidthDip = 340;

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
        _popup.Show(Fit());

        // Measure again once XAML has applied styles (first measurement can be short), so nothing is clipped.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => AppWindow.MoveAndResize(Fit()));
    }

    /// <summary>Bounds sized to the content, bottom-right above the taskbar.</summary>
    private Windows.Graphics.RectInt32 Fit()
    {
        Root.Measure(new Windows.Foundation.Size(WidthDip, double.PositiveInfinity));
        var scale = Root.XamlRoot?.RasterizationScale ?? GetDpiForSystem() / 96.0;
        var w = (int)Math.Ceiling(WidthDip * scale);
        var h = (int)Math.Ceiling((Root.DesiredSize.Height + 4) * scale);
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        return new Windows.Graphics.RectInt32(work.X + work.Width - w - 12, work.Y + work.Height - h - 12, w, h);
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
        PresenceText.Text = s.Current is not null ? "Nearby"
            : shown is null ? "Not heard yet"
            : string.Create(CultureInfo.CurrentCulture, $"Seen {shown.LastSeen.LocalDateTime:t}");

        if (shown is null)
        {
            BatteryText.Text = "-";
            WornText.Text = "Worn: unknown";
            ChargingText.Text = "Charging: unknown";
            Wear.IsWorn = null;
        }
        else
        {
            var st = shown.Status;
            BatteryText.Text = Display.BatteryOf(st, show);
            var worn = Display.Flag(st.AnyWorn, show, "On head", "Off head");
            WornText.Text = worn == "Unknown" ? "Worn: unknown" : worn;
            var charging = Display.Flag(st.Charging, show, "Charging", "Not charging");
            ChargingText.Text = charging == "Unknown" ? "Charging: unknown" : charging;
            Wear.IsWorn = s.Current is not null && Display.IsShown(st.AnyWorn, show) ? st.AnyWorn.Value : null;

            // Last-known values look dimmed, as on the Status page.
            WornText.Opacity = ChargingText.Opacity = s.Current is null ? 0.45 : 1.0;
        }

        var connection = s.UsbConnected ? "USB-C cable"
            : App.Switch?.IsConnected == true ? "Connected to this PC"
            : s.ConnectedTo == Core.Switching.ConnectedTo.OtherDevice ? "On another device"
            : null;
        ConnectionText.Text = connection ?? "";
        ConnectionChip.Visibility = connection is null ? Visibility.Collapsed : Visibility.Visible;

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
