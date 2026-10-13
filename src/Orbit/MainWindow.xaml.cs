using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Orbit.Pages;

namespace Orbit;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 780));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Orbit.ico"));

        // Closing the window keeps Orbit running in the tray; Exit is in the tray menu.
        AppWindow.Closing += (_, e) =>
        {
            if (Application.Current is App { IsExiting: false })
            {
                e.Cancel = true;
                AppWindow.Hide();
            }
        };

        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var pageType = ((args.SelectedItem as NavigationViewItem)?.Tag as string) switch
        {
            "status" => typeof(StatusPage),
            "settings" => typeof(SettingsPage),
            "capture" => typeof(CapturePage),
            _ => null,
        };

        if (pageType is not null && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
