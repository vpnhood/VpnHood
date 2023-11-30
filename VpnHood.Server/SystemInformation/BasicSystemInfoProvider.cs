namespace VpnHood.Server.SystemInformation;

public class BasicSystemInfoProvider : ISystemInfoProvider
{
    public string GetOperatingSystemInfo()
    {
        return Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");
    }

    public SystemInfo GetSystemInfo()
    {
        return new SystemInfo
        {
            OsInfo = Environment.OSVersion.ToString(),
            TotalMemory = null,
            AvailableMemory = null,
            CpuUsage = null,
            LogicalCoreCount = Environment.ProcessorCount
        };
    }
}