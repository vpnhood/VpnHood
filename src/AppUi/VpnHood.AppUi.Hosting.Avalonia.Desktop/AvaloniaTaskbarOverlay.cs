using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppUi.Hosting.Avalonia.Desktop;

// Windows' overlay icon on the window's taskbar button (ITaskbarList3.SetOverlayIcon), which Avalonia has no API
// for. The taskbar takes one only once it has made the button, and says so with the message TaskbarButtonCreated -
// again when the window comes back from the tray, or Explorer restarts - so the icon is kept and set again then.
// Refused in an AppContainer (Access is denied), where the shell shows Windows' own badge instead. On the window's
// thread.
[SupportedOSPlatform("windows")]
internal sealed class AvaloniaTaskbarOverlay : IDisposable
{
    private const int SmCxSmIcon = 49;
    private const uint MsgFltAllow = 1;
    private const uint IconResourceVersion = 0x00030000;
    private static readonly uint ButtonCreatedMessage = RegisterWindowMessageW("TaskbarButtonCreated");
    private readonly Window _window;
    private readonly IntPtr _hWnd;
    private readonly ITaskbarList3 _taskbar;
    private readonly Win32Properties.CustomWndProcHookCallback _wndProcHook;
    private IntPtr _icon;
    private string? _description;

    // The size the taskbar draws an overlay at: a small icon at the system's scaling.
    public int IconSize { get; } = GetSystemMetrics(SmCxSmIcon);

    public AvaloniaTaskbarOverlay(Window window)
    {
        _window = window;
        _hWnd = window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("The window has no handle.");
        // a COM object: the cast is its QueryInterface
        // ReSharper disable once SuspiciousTypeConversion.Global
        var taskbar = (ITaskbarList3)new TaskbarList();
        taskbar.HrInit();
        _taskbar = taskbar;

        // an elevated window still hears it from an Explorer that is not
        ChangeWindowMessageFilterEx(_hWnd, ButtonCreatedMessage, MsgFltAllow, IntPtr.Zero);
        _wndProcHook = WndProcHook;
        Win32Properties.AddWndProcHookCallback(window, _wndProcHook);
    }

    // The icon, out of a PNG of IconSize, and its words for a screen reader.
    public void SetIcon(byte[] png, string description)
    {
        var icon = CreateIconFromResourceEx(png, (uint)png.Length, true, IconResourceVersion, IconSize, IconSize, 0);
        if (icon == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        // the old one goes once the taskbar has the new one
        var oldIcon = _icon;
        _icon = icon;
        _description = description;
        Apply();
        if (oldIcon != IntPtr.Zero)
            DestroyIcon(oldIcon);
    }

    // Without a button - the window in the tray - the taskbar refuses it, and takes it once the button is made.
    private void Apply()
    {
        var result = _taskbar.SetOverlayIcon(_hWnd, _icon, _description);
        if (result < 0)
            VhLogger.Instance.LogDebug("The taskbar took no overlay icon yet. HRESULT: 0x{Result:X8}", result);
    }

    private IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == ButtonCreatedMessage)
            Apply();

        return IntPtr.Zero;
    }

    [DllImport("user32", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("user32")]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action, IntPtr changeFilter);

    [DllImport("user32")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx(byte[] resourceBits, uint resourceSize, bool isIcon,
        uint version, int width, int height, uint flags);

    [DllImport("user32")]
    private static extern bool DestroyIcon(IntPtr icon);

    public void Dispose()
    {
        Win32Properties.RemoveWndProcHookCallback(_window, _wndProcHook);
        if (_icon != IntPtr.Zero)
            DestroyIcon(_icon);
        _icon = IntPtr.Zero;
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    private class TaskbarList;

    // ITaskbarList, ITaskbarList2 and ITaskbarList3 in their table's order, as far as SetOverlayIcon
    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hWnd);
        void DeleteTab(IntPtr hWnd);
        void ActivateTab(IntPtr hWnd);
        void SetActiveAlt(IntPtr hWnd);
        void MarkFullscreenWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hWnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hWnd, int flags);
        void RegisterTab(IntPtr hWndTab, IntPtr hWndMdi);
        void UnregisterTab(IntPtr hWndTab);
        void SetTabOrder(IntPtr hWndTab, IntPtr hWndInsertBefore);
        void SetTabActive(IntPtr hWndTab, IntPtr hWndMdi, uint reserved);
        void ThumbBarAddButtons(IntPtr hWnd, uint count, IntPtr buttons);
        void ThumbBarUpdateButtons(IntPtr hWnd, uint count, IntPtr buttons);
        void ThumbBarSetImageList(IntPtr hWnd, IntPtr imageList);

        [PreserveSig]
        int SetOverlayIcon(IntPtr hWnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string? description);
    }
}
