using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Orbit.Platform;

/// <summary>
/// Desktop toast notifications through the classic Windows.UI.Notifications API. Used because the Windows App SDK's
/// AppNotificationManager.Register() fails in Orbit's unpackaged, self-contained build (0x8007007E "module not found",
/// logged 2026-10-14). An unpackaged app can show toasts once its AppUserModelID is registered under
/// HKCU\Software\Classes\AppUserModelId with a display name and icon, and the process uses that ID.
/// Only per-user registry keys owned by Orbit are written.
/// </summary>
public static class Toasts
{
    public const string AppId = "Orbit.App";

    private static ToastNotifier? _notifier;

    /// <summary>Registers Orbit's AppUserModelID (display name + icon) for the current user. Safe to call repeatedly.</summary>
    public static void Register(string displayName, string iconPath)
    {
        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}"))
        {
            key.SetValue("DisplayName", displayName);
            key.SetValue("IconUri", iconPath);
        }

        SetCurrentProcessExplicitAppUserModelID(AppId);
        _notifier = ToastNotificationManager.CreateToastNotifier(AppId);
    }

    public static void Show(string title, string body)
    {
        if (_notifier is null)
        {
            throw new InvalidOperationException("Toasts.Register was not called.");
        }

        var xml = new XmlDocument();
        xml.LoadXml(
            "<toast><visual><binding template=\"ToastGeneric\">" +
            $"<text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(body)}</text>" +
            "</binding></visual></toast>");
        _notifier.Show(new ToastNotification(xml));
    }

    /// <summary>Removes Orbit's notification registration (used by an uninstall/cleanup path).</summary>
    public static void Unregister() => Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\AppUserModelId\{AppId}", throwOnMissingSubKey: false);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
