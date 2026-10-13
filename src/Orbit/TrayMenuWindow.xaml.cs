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

    public TrayMenuWindow()
    {
        InitializeComponent();
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsAlwaysOnTop = true;
            p.SetBorderAndTitleBar(true, false);
        }

        AppWindow.IsShownInSwitchers = false;
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                AppWindow.Hide();
            }
        };
    }

    public void ShowAtCursor()
    {
        Render();

        var root = (FrameworkElement)Content;
        root.Measure(new Windows.Foundation.Size(WidthDip, double.PositiveInfinity));
        var scale = root.XamlRoot?.RasterizationScale ?? GetScaleForCursor();
        var w = (int)Math.Ceiling(WidthDip * scale);
        var h = (int)Math.Ceiling((root.DesiredSize.Height + 2) * scale);

        GetCursorPos(out var cursor);
        var work = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary).WorkArea;

        // Open up-and-left of the cursor (the tray is usually bottom-right), kept inside the work area.
        var x = Math.Clamp(cursor.X - w, work.X, work.X + work.Width - w);
        var y = Math.Clamp(cursor.Y - h, work.Y, work.Y + work.Height - h);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, w, h));
        AppWindow.Show();
        Activate();
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
        AppWindow.Hide();
        _ = App.Switch?.SwitchToThisPcAsync();
    }

    private void OnRelease(object sender, RoutedEventArgs e)
    {
        AppWindow.Hide();
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
        AppWindow.Hide();
        (Application.Current as App)?.ShowMainWindow();
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        AppWindow.Hide();
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
