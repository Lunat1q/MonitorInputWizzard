using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace MonitorInputWizzard;

/// <summary>Registers system-wide hotkeys via Win32 RegisterHotKey and fires a callback on press.</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyManager(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _hwnd = helper.EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd)!;
        _source.AddHook(WndProc);
    }

    /// <summary>Registers a hotkey. Returns its id, or null if the OS rejected it (e.g. already taken).</summary>
    public int? Register(ModifierKeys mods, Key key, Action onPressed)
    {
        int id = _nextId++;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        // MOD_NOREPEAT (0x4000) stops auto-repeat while held.
        if (!RegisterHotKey(_hwnd, id, (uint)mods | 0x4000, vk)) return null;
        _actions[id] = onPressed;
        return id;
    }

    public void Unregister(int id)
    {
        if (_actions.Remove(id)) UnregisterHotKey(_hwnd, id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_hwnd, id);
        _actions.Clear();
        _source.RemoveHook(WndProc);
    }
}
