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
        _switch.Completed += (_, r) =>
        {
            if (r.Outcome is not Core.Switching.SwitchOutcome.AlreadyConnected and not Core.Switching.SwitchOutcome.AlreadyReleased)
            {
                Notify("Orbit", r.Message);
            }
        };

        CreateTray();

        var startInTray = Environment.GetCommandLineArgs().Contains("--tray");
        if (!startInTray)
        {
            ShowMainWindow();
        }

        await _service.StartAsync();
        await _switch.SyncAsync();
    }

    public void ShowMainWindow()
    {
        _window ??= new MainWindow();
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
        _window?.Close();
        Exit();
    }

    private void CreateTray()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Open Orbit", Command = new RelayCommand(ShowMainWindow) });
        menu.Items.Add(new MenuFlyoutItem { Text = "Switch to this PC", Command = new RelayCommand(() => _ = _switch?.SwitchToThisPcAsync()) });
        menu.Items.Add(new MenuFlyoutItem { Text = "Release to other device", Command = new RelayCommand(() => _ = _switch?.ReleaseAsync()) });
        menu.Items.Add(new MenuFlyoutItem { Text = "Toggle auto-pause", Command = new RelayCommand(ToggleAutoPause) });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem { Text = "Exit", Command = new RelayCommand(ExitApp) });

        _tray = new TaskbarIcon
        {
            ToolTipText = "Orbit",
            IconSource = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Orbit.ico"))),
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(ShowFlyout),
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
        };
        _tray.ForceCreate(enablesEfficiencyMode: false);
    }

    private void ShowFlyout()
    {
        _flyout ??= new FlyoutWindow();
        _flyout.ShowNearTray();
    }

    private void ToggleAutoPause()
    {
        if (_service is null)
        {
            return;
        }

        _service.Settings.AutoPause = !_service.Settings.AutoPause;
        _service.SaveSettings();
        Notify("Orbit", _service.Settings.AutoPause ? "Auto-pause on" : "Auto-pause off");
    }

    private void UpdateTray()
    {
        if (_tray is not null && _service is not null)
        {
            _tray.ToolTipText = Display.Tooltip(_service.DisplayName, _service.Current, _service.LastKnown, _service.Settings.ShowUnverified);
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
