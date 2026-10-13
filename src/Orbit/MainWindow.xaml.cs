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

        ContentFrame.NavigationFailed += (_, e) =>
        {
            Services.Log.Error($"navigation to {e.SourcePageType?.Name}", e.Exception);
            e.Handled = true;
        };

        Nav.SelectedItem = Nav.MenuItems[0];
    }

    /// <summary>Selects the nav item with the given tag ("status", "settings", "capture").</summary>
    public void NavigateTo(string tag)
    {
        var item = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(i => (string)i.Tag == tag);
        if (item is not null)
        {
            Nav.SelectedItem = item;
        }
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
            try
            {
                ContentFrame.Navigate(pageType);
            }
            catch (Exception ex)
            {
                Services.Log.Error($"navigate {pageType.Name}", ex);
            }
        }
    }
}
