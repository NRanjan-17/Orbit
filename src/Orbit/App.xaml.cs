using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Orbit.Core.Media;
using Orbit.Core.Settings;
using Orbit.Services;

namespace Orbit;

public partial class App : Application
{
    private static Mutex? _singleInstance;
    private MainWindow? _window;
    private FlyoutWindow? _flyout;
    private TaskbarIcon? _tray;
    private OrbitService? _service;
    private SwitchService? _switch;
    private bool _notificationsRegistered;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Write($"ERROR unhandled: {e.Message}\n{e.Exception}");
            e.Handled = true; // keep the tray app alive; the error is in orbit.log
        };

        // WinUI often reports crashes without a managed stack. Record where argument/COM errors are first
        // thrown so a crash can be traced to the calling code.
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            if (e.Exception is ArgumentException or System.Runtime.InteropServices.COMException)
            {
                Log.Write($"first-chance {e.Exception.GetType().Name}: {e.Exception.Message}\n{Environment.StackTrace}");
            }
        };
    }

    /// <summary>
    /// "--selftest": opens the tray menu, the flyout and Settings on a timer so UI crashes can be reproduced
    /// without clicking. Development aid only.
    /// </summary>
    private async Task RunSelfTestAsync()
    {
        var steps = new (string Name, Action Act)[]
        {
            ("tray menu", ShowTrayMenu),
            ("tray menu again (toggle closed)", ShowTrayMenu),
            ("flyout", ShowFlyout),
            ("settings page", () => ShowMainWindow("settings")),
            ("status page", () => ShowMainWindow("status")),
        };

        foreach (var (name, act) in steps)
        {
            await Task.Delay(2500);
            Log.Write($"selftest: {name}");
            act();
        }

        await Task.Delay(2500);
        Log.Write("selftest: done");
    }

    public static OrbitService? Service => (Current as App)?._service;

    public static SwitchService? Switch => (Current as App)?._switch;

    public static MainWindow? MainWindow => (Current as App)?._window;

    /// <summary>True while exiting for real (closing the main window otherwise just hides it to the tray).</summary>
    public bool IsExiting { get; private set; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstance = new Mutex(true, @"Local\Orbit.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Exit();
            return;
        }

        RegisterNotifications();

        _service = new OrbitService(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(),
            new SettingsStore(Path.Combine(AppPaths.DataDirectory, "settings.json")));
        _service.Changed += (_, _) => UpdateTray();
        _service.LowBattery += (_, e) => Notify($"{e.Name}: battery low", $"About {Core.Parsing.ProximityPairingParser.FormatBattery(e.Percent)} left.");
        _service.AutoPaused += (_, a) => UpdateTray();

        _switch = new SwitchService(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread(), _service);
        _switch.Completed += async (_, r) =>
        {
            if (r.Outcome is not Core.Switching.SwitchOutcome.AlreadyConnected and not Core.Switching.SwitchOutcome.AlreadyReleased)
            {
                Notify("Orbit", r.Message);
            }

            if (r.Outcome == Core.Switching.SwitchOutcome.Connected && _service.Settings.ChimeOnConnect)
            {
                // Give Windows a moment to route the new A2DP output before playing.
                await Task.Delay(1500);
                await PlayChimeAsync();
            }
        };
        _service.WornChanged += async (_, worn) =>
        {
            if (worn && _service.Settings.ChimeOnPutOn && _switch.IsConnected)
            {
                await PlayChimeAsync();
            }
        };

        _service.Changed += (_, _) => NotifyConnectionChanges();
        _switch.Changed += (_, _) => NotifyConnectionChanges();

        CreateTray();

        var startInTray = Environment.GetCommandLineArgs().Contains("--tray");
        if (!startInTray)
        {
            ShowMainWindow();
        }

        await _service.StartAsync();
        await _switch.SyncAsync();

        if (Environment.GetCommandLineArgs().Contains("--selftest"))
        {
            await RunSelfTestAsync();
        }
    }

    public void ShowMainWindow() => ShowMainWindow(null);

    /// <param name="page">Optional page tag to open: "status", "settings" or "capture".</param>
    public void ShowMainWindow(string? page)
    {
        _window ??= new MainWindow();
        if (page is not null)
        {
            _window.NavigateTo(page);
        }

        _window.AppWindow.Show();
        _window.Activate();
    }

    public void ExitApp()
    {
        IsExiting = true;
        _tray?.Dispose();
        _switch?.Dispose();
        _service?.Dispose();
        if (_notificationsRegistered)
        {
            AppNotificationManager.Default.Unregister();
        }

        _flyout?.Close();
        _trayMenu?.Close();
        _window?.Close();
        Exit();
    }

    private void CreateTray()
    {
        _tray = new TaskbarIcon
        {
            ToolTipText = "Orbit",
            IconSource = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Orbit.ico"))),
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(ShowFlyout),
            // Right-click opens Orbit's own menu window (H.NotifyIcon's WinUI menu rendered clipped).
            RightClickCommand = new RelayCommand(ShowTrayMenu),
        };
        _tray.ForceCreate(enablesEfficiencyMode: false);
    }

    private TrayMenuWindow? _trayMenu;

    private void ShowTrayMenu()
    {
        _trayMenu ??= new TrayMenuWindow();
        _trayMenu.ShowAtCursor();
    }

    private void ShowFlyout()
    {
        _flyout ??= new FlyoutWindow();
        _flyout.ShowNearTray();
    }

    private void UpdateTray()
    {
        if (_tray is not null && _service is not null)
        {
            _tray.ToolTipText = Display.Tooltip(_service.DisplayName, _service.Current, _service.LastKnown, _service.Settings.ShowUnverified);
            UpdateTrayIcon();
        }
    }

    private bool? _lastUsb;
    private bool? _lastBluetooth;

    /// <summary>
    /// Toast when the headphones newly connect by USB-C cable or Bluetooth audio. The first observation after
    /// start-up only records the state, so already-connected headphones don't trigger a notification.
    /// </summary>
    private void NotifyConnectionChanges()
    {
        if (_service is null || _switch is null)
        {
            return;
        }

        var name = _service.DisplayName;
        var usb = _service.UsbConnected;
        if (_lastUsb is { } wasUsb && usb != wasUsb && _service.Settings.ConnectToast)
        {
            Notify(name, usb ? "Connected by USB-C cable." : "USB-C cable disconnected.");
        }

        _lastUsb = usb;

        // Bluetooth state is only meaningful once switching knows the device.
        if (_switch.IsAvailable)
        {
            var bt = _switch.IsConnected;
            if (_lastBluetooth is { } wasBt && bt && !wasBt && _service.Settings.ConnectToast)
            {
                Notify(name, "Connected to this PC over Bluetooth.");
            }

            _lastBluetooth = bt;
        }
    }

    private string? _trayIconKey;

    /// <summary>
    /// Orbit logo, or (Settings → "Show battery in the tray icon") the battery number drawn as the icon:
    /// exact % when available, otherwise the lower end of the 10 % band; logo when nothing is known.
    /// </summary>
    private void UpdateTrayIcon()
    {
        var s = (_service!.Current ?? _service.LastKnown)?.Status;
        int? percent = s is null ? null
            : s.ExactBattery.HasValue ? s.ExactBattery.Value
            : Display.IsShown(s.LowestBattery, _service.Settings.ShowUnverified) ? s.LowestBattery.Value
            : null;

        var light = TaskbarUsesLightTheme();
        var key = _service.Settings.TrayShowsBattery && percent is { } p ? $"{p}|{light}" : "logo";
        if (key == _trayIconKey)
        {
            return;
        }

        _trayIconKey = key;
        if (key == "logo")
        {
            _tray!.IconSource = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Orbit.ico")));
            return;
        }

        var value = percent!.Value;
        var color = value <= _service.Settings.LowBatteryPercent
            ? System.Drawing.Color.FromArgb(255, 255, 140, 0)            // orange when low
            : light ? System.Drawing.Color.Black : System.Drawing.Color.White;
        try
        {
            var file = TrayIconRenderer.Render(value.ToString(System.Globalization.CultureInfo.InvariantCulture), color);
            _tray!.IconSource = new BitmapImage(new Uri(file));
        }
        catch (Exception ex)
        {
            Log.Error("tray battery icon", ex);
            _trayIconKey = null;
        }
    }

    /// <summary>Taskbar theme (HKCU ...\Themes\Personalize\SystemUsesLightTheme); true = light taskbar.</summary>
    private static bool TaskbarUsesLightTheme()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
    }

    public async Task PlayChimeAsync()
    {
        if (_service?.BoundDevice?.ContainerId is not { } container)
        {
            return;
        }

        try
        {
            await Platform.Audio.ChimePlayer.PlayAsync(container);
        }
        catch (Exception)
        {
            // A chime is cosmetic; never let it break switching or auto-pause.
        }
    }

    private void RegisterNotifications()
    {
        try
        {
            AppNotificationManager.Default.Register();
            _notificationsRegistered = true;
        }
        catch (Exception)
        {
            // Notifications are optional; the app works without them.
        }
    }

    private void Notify(string title, string body)
    {
        if (!_notificationsRegistered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.Show(new AppNotificationBuilder().AddText(title).AddText(body).BuildNotification());
        }
        catch (Exception)
        {
        }
    }
}
