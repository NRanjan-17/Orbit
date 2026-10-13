using System.Runtime.InteropServices;
using Orbit.Core.Switching;

namespace Orbit.Platform;

/// <summary>
/// System-wide hotkey via Win32 RegisterHotKey on a dedicated thread with its own message loop
/// (hWnd = NULL posts WM_HOTKEY to the registering thread's queue), so no window is needed.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int WmQuit = 0x0012;
    private const uint ModNoRepeat = 0x4000;
    private const int HotkeyId = 0x0B17;

    private readonly Thread _thread;
    private readonly TaskCompletionSource<bool> _registered = new();
    private uint _threadId;

    private GlobalHotkey(HotkeyGesture gesture, Action onPressed)
    {
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            var ok = RegisterHotKey(IntPtr.Zero, HotkeyId, (uint)gesture.Modifiers | ModNoRepeat, gesture.VirtualKey);
            _registered.SetResult(ok);
            if (!ok)
            {
                return;
            }

            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.Message == WmHotkey && msg.WParam == HotkeyId)
                {
                    onPressed();
                }
            }

            UnregisterHotKey(IntPtr.Zero, HotkeyId);
        })
        {
            IsBackground = true,
            Name = "Orbit hotkey",
        };
        _thread.Start();
    }

    /// <summary>Registers the hotkey. Returns null if another app already owns this key combination.</summary>
    public static async Task<GlobalHotkey?> RegisterAsync(HotkeyGesture gesture, Action onPressed)
    {
        var hotkey = new GlobalHotkey(gesture, onPressed);
        return await hotkey._registered.Task ? hotkey : null;
    }

    public void Dispose()
    {
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public int Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
