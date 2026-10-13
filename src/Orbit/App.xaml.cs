using Microsoft.UI.Xaml;

namespace Orbit;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    public static MainWindow? MainWindow => (Current as App)?._window;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
