using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Orbit.Services;

/// <summary>
/// Makes a borderless window behave like a popup: takes focus when shown, and hides as soon as anything else
/// is in the foreground. Relying on the Deactivated event alone is not enough: when opened from the tray,
/// Windows may refuse to make the window active, so it never deactivates and stays on screen.
/// </summary>
public sealed class PopupBehavior
{
    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly DispatcherQueueTimer _watch;
    private DateTime _shownAt;

    public PopupBehavior(Window window)
    {
        _window = window;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _watch = window.DispatcherQueue.CreateTimer();
        _watch.Interval = TimeSpan.FromMilliseconds(150);
        _watch.Tick += (_, _) =>
        {
            // Grace period while Windows hands over focus after the tray click.
            if (DateTime.UtcNow - _shownAt > TimeSpan.FromMilliseconds(400) && GetForegroundWindow() != _hwnd)
            {
                Hide();
            }
        };

        window.Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                Hide();
            }
        };
    }

    public bool IsVisible => _window.AppWindow.IsVisible;

    /// <summary>Shows the window at <paramref name="bounds"/> (physical pixels) and gives it focus.</summary>
    public void Show(Windows.Graphics.RectInt32 bounds)
    {
        _window.AppWindow.MoveAndResize(bounds);
        _window.AppWindow.Show();
        _window.Activate();
        SetForegroundWindow(_hwnd);
        _shownAt = DateTime.UtcNow;
        _watch.Start();
    }

    public void Hide()
    {
        _watch.Stop();
        _window.AppWindow.Hide();
    }

    /// <summary>Common popup chrome: no title bar, not resizable, topmost, not in Alt+Tab.</summary>
    public static void ConfigureChrome(Window window)
    {
        if (window.AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsAlwaysOnTop = true;
            p.SetBorderAndTitleBar(true, false);
        }

        window.AppWindow.IsShownInSwitchers = false;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
