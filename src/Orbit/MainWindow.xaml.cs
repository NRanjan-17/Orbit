using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Orbit.Pages;

namespace Orbit;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = false;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 760));
        AppWindow.SetIcon("Assets\\Orbit.ico");
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        var pageType = tag switch
        {
            "capture" => typeof(CapturePage),
            _ => null,
        };

        if (pageType is not null && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }
}
