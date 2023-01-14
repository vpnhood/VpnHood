using VpnHood.Common.Utils;

namespace VpnHood.Server.SystemInformation;

public class SystemInfo
{
    public string OsInfo { get; }
    public long? TotalMemory { get; set; }
    public long? AvailableMemory { get; }
    public int? CpuUsage { get; }
    public int LogicalCoreCount { get; }

    public SystemInfo(string osInfo, long? totalMemory, long? availableMemory, int? cpuUsage, int logicalCoreCount)
    {
        OsInfo = osInfo;
        TotalMemory = totalMemory;
        AvailableMemory = availableMemory;
        CpuUsage = cpuUsage;
        LogicalCoreCount = logicalCoreCount;
    }

    public override string ToString()
    {
        var totalMemory = TotalMemory != null ? Util.FormatBytes(TotalMemory.Value) : "*";
        return $"{OsInfo}, TotalMemory: {totalMemory}, Logical Cores: {LogicalCoreCount}";
    }

}