using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Orbit.Platform;
using Orbit.Services;

namespace Orbit;

/// <summary>
/// Orbit's tray right-click menu: a small borderless window (status header, switch/release, toggles,
/// open/settings/quit). Replaces H.NotifyIcon's SecondWindow MenuFlyout, which rendered clipped to one
/// character per item on the owner's PC. Opens at the cursor and hides when it loses focus.
/// </summary>
public sealed partial class TrayMenuWindow : Window
{
    private const double WidthDip = 300;
    private bool _rendering;

    private readonly PopupBehavior _popup;
    private Windows.Graphics.PointInt32 _anchor;

    public TrayMenuWindow()
    {
        InitializeComponent();
        PopupBehavior.ConfigureChrome(this);
        _popup = new PopupBehavior(this);
    }

    public void ShowAtCursor()
    {
        if (_popup.IsVisible)
        {
            _popup.Hide();
            return;
        }

        Render();
        GetCursorPos(out var cursor);
        _anchor = new Windows.Graphics.PointInt32(cursor.X, cursor.Y);
        _popup.Show(Fit());

        // The first measurement can happen before XAML has applied styles (wrong height); fit again once rendered.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => AppWindow.MoveAndResize(Fit()));
    }

    /// <summary>Window bounds that fit the content, up-and-left of the anchor, inside the work area.</summary>
    private Windows.Graphics.RectInt32 Fit()
    {
        var root = (FrameworkElement)Content;
        root.Measure(new Windows.Foundation.Size(WidthDip, double.PositiveInfinity));
        var scale = root.XamlRoot?.RasterizationScale ?? GetScaleForCursor();
        var w = (int)Math.Ceiling(WidthDip * scale);
        var h = (int)Math.Ceiling((root.DesiredSize.Height + 4) * scale);

        var work = DisplayArea.GetFromPoint(_anchor, DisplayAreaFallback.Primary).WorkArea;
        var x = Math.Clamp(_anchor.X - w, work.X, work.X + work.Width - w);
        var y = Math.Clamp(_anchor.Y - h, work.Y, work.Y + work.Height - h);
        return new Windows.Graphics.RectInt32(x, y, w, h);
    }

    private void Render()
    {
        if (App.Service is not { } s)
        {
            return;
        }

        _rendering = true;
        var show = s.Settings.ShowUnverified;
        NameText.Text = s.BoundDevice is null && s.LastKnown is null ? "No device" : s.DisplayName;

        var shown = s.Current ?? s.LastKnown;
        if (shown is null)
        {
            StatusText.Text = "Not heard yet";
        }
        else
        {
            var st = shown.Status;
            var parts = new List<string> { Display.BatteryOf(st, show) };
            if (Display.IsShown(st.Charging, show) && st.Charging.Value)
            {
                parts.Add("charging");
            }

            if (Display.IsShown(st.AnyWorn, show))
            {
                parts.Add(st.AnyWorn.Value ? "on head" : "off head");
            }

            if (s.Current is null)
            {
                parts.Add("asleep");
            }

            if (s.UsbConnected)
            {
                parts.Add("USB");
            }

            StatusText.Text = string.Join(" · ", parts);
        }

        var sw = App.Switch;
        var connected = sw?.IsConnected == true;
        var available = sw is { IsAvailable: true, IsBusy: false };
        SwitchButton.IsEnabled = available && !connected;
        ReleaseButton.IsEnabled = available && connected;
        SwitchText.Text = connected ? "Connected to this PC" : "Switch to this PC";

        AutoPauseSwitch.IsOn = s.Settings.AutoPause;
        StartupSwitch.IsOn = StartupRegistration.IsEnabled();
        _rendering = false;
    }

    private void OnSwitch(object sender, RoutedEventArgs e)
    {
        _popup.Hide();
        _ = App.Switch?.SwitchToThisPcAsync();
    }

    private void OnRelease(object sender, RoutedEventArgs e)
    {
        _popup.Hide();
        _ = App.Switch?.ReleaseAsync();
    }

    private void OnAutoPauseToggled(object sender, RoutedEventArgs e)
    {
        if (_rendering || App.Service is not { } s)
        {
            return;
        }

        s.Settings.AutoPause = AutoPauseSwitch.IsOn;
        s.SaveSettings();
    }

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_rendering)
        {
            return;
        }

        try
        {
            StartupRegistration.Set(StartupSwitch.IsOn, Environment.ProcessPath!);
            if (App.Service is { } s)
            {
                s.Settings.StartWithWindows = StartupSwitch.IsOn;
                s.SaveSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Error("toggle startup", ex);
        }
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        _popup.Hide();
        (Application.Current as App)?.ShowMainWindow();
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        _popup.Hide();
        (Application.Current as App)?.ShowMainWindow("settings");
    }

    private void OnExit(object sender, RoutedEventArgs e) => (Application.Current as App)?.ExitApp();

    private static double GetScaleForCursor() => GetDpiForSystem() / 96.0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
}
