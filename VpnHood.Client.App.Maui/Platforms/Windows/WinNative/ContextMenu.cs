using System.Runtime.InteropServices;

// ReSharper disable once CheckNamespace
namespace WinNative
{
    public class ContextMenu : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out PointFx lpPoint);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool AppendMenu(IntPtr hMenu, MenuFlags uFlags, uint idNewItem, string lpNewItem);
        [DllImport("user32.dll")]
        private static extern bool TrackPopupMenuEx(IntPtr hMenu, uint fuFlags, int x, int y, IntPtr hWnd, IntPtr tpm);
        public struct Fixed
        {
            public short Fraction;
            public short Value;
        }
        public struct PointFx
        {
            public Fixed X;
            public Fixed Y;
        }

        private readonly Window _wnd;
        private IntPtr Handle { get; }
        public bool IsRightToLeft { get; set; }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            const uint wmCommand = 0x111;
            if (msg == wmCommand && wParam < _eventHandlers.Count)
                _eventHandlers[(int)wParam].Invoke(this, EventArgs.Empty);

            return Window.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        [Flags]
        public enum MenuFlags : uint
        {
            String = 0,
            ByPosition = 0x400,
            Separator = 0x800,
            Remove = 0x1000,
        }

        public ContextMenu()
        {
            _wnd = new Window(WndProc);
            Handle = CreatePopupMenu();
        }

        public void Show()
        {
            GetCursorPos(out var pt);
            Show(pt.X.Fraction, pt.Y.Fraction);
        }

        public void Show(int x, int y)
        {
            const uint tpmLayoutRtl = 0x00008000;
            var flag = (uint)0;
            if (IsRightToLeft) flag |= tpmLayoutRtl;

            SetForegroundWindow(_wnd.Handle);
            TrackPopupMenuEx(Handle, flag, x, y, _wnd.Handle, IntPtr.Zero);
        }

        private readonly List<EventHandler> _eventHandlers = new();
        public void AddMenuItem(string text, EventHandler onClick)
        {
            _eventHandlers.Add(onClick);
            AppendMenu(Handle, MenuFlags.ByPosition | MenuFlags.String, (uint)_eventHandlers.Count - 1, text);
        }

        public void AddMenuSeparator()
        {
            AppendMenu(Handle, MenuFlags.ByPosition | MenuFlags.Separator, (uint)_eventHandlers.Count - 1, string.Empty);
        }

        #region IDisposable Support
        private bool _disposedValue; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (_disposedValue) return;
            if (disposing)
            {
                // dispose managed state (managed objects).
                _wnd.Dispose();
            }

            // free unmanaged resources (unmanaged objects) and override a finalizer below.
            // set large fields to null.
            DestroyMenu(Handle);
            _disposedValue = true;
        }

        ~ContextMenu()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
