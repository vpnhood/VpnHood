using System;
using System.Runtime.InteropServices;
using VpnHood.Server.SystemInformation;
using System.Diagnostics;
using VpnHood.Common.Logging;
using Microsoft.Extensions.Logging;
#pragma warning disable CA1416

// ReSharper disable MemberCanBePrivate.Local
// ReSharper disable StructCanBeMadeReadOnly

namespace VpnHood.Server.App.SystemInformation;

public class WinSystemInfoProvider : ISystemInfoProvider
{
    private readonly PerformanceCounter _cpuCounter = new ("Processor", "% Processor Time", "_Total");

    public SystemInfo GetSystemInfo()
    {
        try
        {

            var availableMemoryCounter = new PerformanceCounter("Memory", "Available Bytes");
            var availableMemory = availableMemoryCounter.RawValue;
            var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            var usage = _cpuCounter.NextValue();
            return new SystemInfo(GetOperatingSystemInfo(), 
                totalMemory, availableMemory, 
                (int)usage,Environment.ProcessorCount);

        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(ex, "Could not get SystemInfo.");
            return new SystemInfo(GetOperatingSystemInfo(), 
                0, 0,
                0, Environment.ProcessorCount);
        }

    }

    public static string GetOperatingSystemInfo()
    {
        return RuntimeInformation.OSDescription.Replace("Microsoft", "").Trim();
    }
}