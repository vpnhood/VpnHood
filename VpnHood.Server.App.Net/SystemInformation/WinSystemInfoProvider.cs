using System;
using System.Runtime.InteropServices;

namespace VpnHood.Server.SystemInformation
{
    public class WinSystemInfoProvider : ISystemInfoProvider
    {

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPerformanceInfo([Out] out PerformanceInformation PerformanceInformation, [In] int Size);

        [StructLayout(LayoutKind.Sequential)]
        private struct PerformanceInformation
        {
            public int Size;
            public IntPtr CommitTotal;
            public IntPtr CommitLimit;
            public IntPtr CommitPeak;
            public IntPtr PhysicalTotal;
            public IntPtr PhysicalAvailable;
            public IntPtr SystemCache;
            public IntPtr KernelTotal;
            public IntPtr KernelPaged;
            public IntPtr KernelNonPaged;
            public IntPtr PageSize;
            public int HandlesCount;
            public int ProcessCount;
            public int ThreadCount;
        }

        public SystemInfo GetSystemInfo()
        {
            long totalMemory = 0;
            long freeMemory = 0;

            if (GetPerformanceInfo(out var pi, Marshal.SizeOf<PerformanceInformation>()))
            {
                freeMemory = Convert.ToInt64(pi.PhysicalAvailable.ToInt64() * pi.PageSize.ToInt64());
                totalMemory = Convert.ToInt64(pi.PhysicalTotal.ToInt64() * pi.PageSize.ToInt64());
            }

            return new SystemInfo(totalMemory, freeMemory);
        }

        public string GetOperatingSystemInfo()
            => Environment.OSVersion.ToString() + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    }
}
