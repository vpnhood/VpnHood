using System;
using System.Runtime.InteropServices;
using VpnHood.Server.SystemInformation;

// ReSharper disable MemberCanBePrivate.Local
// ReSharper disable StructCanBeMadeReadOnly

namespace VpnHood.Server.App.SystemInformation;

public class WinSystemInfoProvider : ISystemInfoProvider
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public readonly int Size;
        public readonly IntPtr CommitTotal;
        public readonly IntPtr CommitLimit;
        public readonly IntPtr CommitPeak;
        public readonly IntPtr PhysicalTotal;
        public readonly IntPtr PhysicalAvailable;
        public readonly IntPtr SystemCache;
        public readonly IntPtr KernelTotal;
        public readonly IntPtr KernelPaged;
        public readonly IntPtr KernelNonPaged;
        public readonly IntPtr PageSize;
        public readonly int HandlesCount;
        public readonly int ProcessCount;
        public readonly int ThreadCount;
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
    {
        return $"{Environment.OSVersion}, {RuntimeInformation.OSDescription}";
    }

    // ReSharper disable once StringLiteralTypo
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo([Out] out PerformanceInformation performanceInformation,
        [In] int size);
}