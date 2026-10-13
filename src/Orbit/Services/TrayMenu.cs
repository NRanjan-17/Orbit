using Microsoft.UI.Xaml.Controls;
using Orbit.Platform;

namespace Orbit.Services;

/// <summary>
/// The tray icon's context menu: a status header (device, battery, worn, connection), switching actions,
/// quick toggles and app commands. Rendered as a WinUI MenuFlyout (H.NotifyIcon "SecondWindow" mode) so it
/// gets Fluent styling, icons and check marks. Text is refreshed whenever Orbit's state changes.
/// </summary>
public sealed class TrayMenu
{
    private readonly App _app;
    private readonly MenuFlyoutItem _header;
    private readonly MenuFlyoutItem _status;
    private readonly MenuFlyoutItem _switch;
    private readonly MenuFlyoutItem _release;
    private readonly ToggleMenuFlyoutItem _autoPause;
    private readonly ToggleMenuFlyoutItem _startup;

    public TrayMenu(App app)
    {
        _app = app;
        _header = Item("Orbit", "", null);
        _header.IsEnabled = false;
        _status = Item("Not heard yet", "", null); // battery glyph
        _status.IsEnabled = false;

        _switch = Item("Switch to this PC", "", () => _ = App.Switch?.SwitchToThisPcAsync());
        _release = Item("Release to other device", "", () => _ = App.Switch?.ReleaseAsync());

        _autoPause = new ToggleMenuFlyoutItem { Text = "Auto-pause", Icon = Glyph(""), Command = new RelayCommand(ToggleAutoPause) };
        _startup = new ToggleMenuFlyoutItem { Text = "Start with Windows", Icon = Glyph(""), Command = new RelayCommand(ToggleStartup) };

        Flyout = new MenuFlyout();
        foreach (var item in new MenuFlyoutItemBase[]
        {
            _header,
            _status,
            new MenuFlyoutSeparator(),
            _switch,
            _release,
            new MenuFlyoutSeparator(),
            _autoPause,
            _startup,
            new MenuFlyoutSeparator(),
            Item("Open Orbit", "", () => _app.ShowMainWindow()),
            Item("Settings", "", () => _app.ShowMainWindow("settings")),
            new MenuFlyoutSeparator(),
            Item("Exit", "", _app.ExitApp),
        })
        {
            Flyout.Items.Add(item);
        }

        Refresh();
    }

    public MenuFlyout Flyout { get; }

    /// <summary>Updates header text, enabled states and check marks from current state.</summary>
    public void Refresh()
    {
        var service = App.Service;
        var sw = App.Switch;
        if (service is null)
        {
            return;
        }

        var show = service.Settings.ShowUnverified;
        _header.Text = service.BoundDevice is null && service.LastKnown is null ? "Orbit: no device" : service.DisplayName;

        var shown = service.Current ?? service.LastKnown;
        if (shown is null)
        {
            _status.Text = "Not heard yet";
        }
        else
        {
            var s = shown.Status;
            var parts = new List<string> { "Battery " + Display.BatteryOf(s, show) };
            if (Display.IsShown(s.Charging, show) && s.Charging.Value)
            {
                parts.Add("charging");
            }

            if (Display.IsShown(s.AnyWorn, show))
            {
                parts.Add(s.AnyWorn.Value ? "on head" : "off head");
            }

            if (service.Current is null)
            {
                parts.Add("asleep");
            }

            _status.Text = string.Join(" · ", parts);
        }

        var connected = sw?.IsConnected == true;
        var available = sw?.IsAvailable == true && sw.IsBusy == false;
        _switch.IsEnabled = available && !connected;
        _release.IsEnabled = available && connected;
        _switch.Text = connected ? "Connected to this PC" : "Switch to this PC";

        _autoPause.IsChecked = service.Settings.AutoPause;
        _startup.IsChecked = StartupRegistration.IsEnabled();
    }

    private void ToggleAutoPause()
    {
        if (App.Service is { } s)
        {
            s.Settings.AutoPause = !s.Settings.AutoPause;
            s.SaveSettings();
        }

        Refresh();
    }

    private void ToggleStartup()
    {
        var enable = !StartupRegistration.IsEnabled();
        try
        {
            StartupRegistration.Set(enable, Environment.ProcessPath!);
            if (App.Service is { } s)
            {
                s.Settings.StartWithWindows = enable;
                s.SaveSettings();
            }
        }
        catch (Exception ex)
        {
            Log.Error("toggle startup", ex);
        }

        Refresh();
    }

    private static MenuFlyoutItem Item(string text, string glyph, Action? action) => new()
    {
        Text = text,
        Icon = Glyph(glyph),
        Command = action is null ? null : new RelayCommand(action),
    };

    private static FontIcon Glyph(string glyph) => new() { Glyph = glyph };
}
