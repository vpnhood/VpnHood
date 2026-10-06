using System.Runtime.InteropServices;

// ReSharper disable UnusedMember.Local
namespace VpnHood.AppUi.Hosting.WebView.Windows.WinNative;

// The badge on the window's taskbar button (ITaskbarList3.SetOverlayIcon), which WPF's TaskbarItemInfo
// drew. The taskbar takes one only after it has made the button, and says so with the message
// TaskbarButtonCreated - again after Explorer restarts - so the badge is kept and set again then.
// On the window's thread.
internal sealed class TaskbarOverlay
{
    private const uint MsgFltAllow = 1;
    private readonly IntPtr _hWnd;
    private ITaskbarList3? _taskbar;
    private IntPtr _icon;

    public static uint ButtonCreatedMessage { get; } = RegisterWindowMessageW("TaskbarButtonCreated");

    public TaskbarOverlay(IntPtr hWnd)
    {
        _hWnd = hWnd;

        // an elevated window still hears it from an Explorer that is not
        ChangeWindowMessageFilterEx(hWnd, ButtonCreatedMessage, MsgFltAllow, IntPtr.Zero);
    }

    // the icon, or none for IntPtr.Zero
    public void SetIcon(IntPtr icon)
    {
        _icon = icon;
        _taskbar?.SetOverlayIcon(_hWnd, icon, null);
    }

    public void OnButtonCreated()
    {
        if (_taskbar == null) {
            var taskbar = (ITaskbarList3)new TaskbarList();
            taskbar.HrInit();
            _taskbar = taskbar;
        }

        _taskbar.SetOverlayIcon(_hWnd, _icon, null);
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
    private class TaskbarList { }

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
        void SetOverlayIcon(IntPtr hWnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string? description);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("user32.dll")]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action, IntPtr changeFilter);
}
