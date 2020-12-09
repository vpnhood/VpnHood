using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace VpnHood.Client.App
{
    internal static class WinConsole
    {
        private const int STD_OUTPUT_HANDLE = -11;
        public const uint ATTACH_PARENT_PROCESS = 0x0ffffffff;

        [Flags]
        public enum ConsoleModes : uint
        {
            ENABLE_PROCESSED_INPUT = 0x0001,
            ENABLE_LINE_INPUT = 0x0002,
            ENABLE_ECHO_INPUT = 0x0004,
            ENABLE_WINDOW_INPUT = 0x0008,
            ENABLE_MOUSE_INPUT = 0x0010,
            ENABLE_INSERT_MODE = 0x0020,
            ENABLE_QUICK_EDIT_MODE = 0x0040,
            ENABLE_EXTENDED_FLAGS = 0x0080,
            ENABLE_AUTO_POSITION = 0x0100,

            //ENABLE_PROCESSED_OUTPUT = 0x0001,
            //ENABLE_WRAP_AT_EOL_OUTPUT = 0x0002,
            //ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004,
            //DISABLE_NEWLINE_AUTO_RETURN = 0x0008,
            //ENABLE_LVB_GRID_WORLDWIDE = 0x0010
        }

       
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        public delegate bool ConsoleCtrlHandlerDelegate(int sig);

        [DllImport("kernel32.dll", EntryPoint = "GetStdHandle", SetLastError = true, CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", EntryPoint = "AllocConsole", SetLastError = true, CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
        public static extern int AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AttachConsole(uint dwProcessId);

        [DllImport("Kernel32")]
        public static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandlerDelegate handler, bool add);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        public static void ShowNewConsole()
        {
            var _ = AllocConsole();
            ShowConsoleInternal();
        }

        public static bool ShowAttachConsole()
        {
            if (AttachConsole(ATTACH_PARENT_PROCESS))
            {
                ShowConsoleInternal();
                return true;
            }
            return false;
        }

        private static void ShowConsoleInternal()
        {
            var stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
            var safeFileHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, true);
            var fileStream = new FileStream(safeFileHandle, FileAccess.Write);
            var standardOutput = new StreamWriter(fileStream, Encoding.ASCII)
            {
                AutoFlush = true
            };
            Console.SetOut(standardOutput);
        }
    }
}
