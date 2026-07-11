namespace VpnHood.Core.Server.SystemInformation;

public class BasicSystemInfoProvider : ISystemInfoProvider
{
    public SystemInfo GetSystemInfo()
    {
        return new SystemInfo {
            OsInfo = Environment.OSVersion.ToString(),
            TotalMemory = null,
            AvailableMemory = null,
            CpuUsage = null,
            LogicalCoreCount = Environment.ProcessorCount
        };
    }
}