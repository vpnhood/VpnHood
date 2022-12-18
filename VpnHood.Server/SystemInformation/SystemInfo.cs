namespace VpnHood.Server.SystemInformation;

public class SystemInfo
{
    public SystemInfo(long? totalMemory, long? availableMemory, int? cpuUsage)
    {
        TotalMemory = totalMemory;
        AvailableMemory = availableMemory;
        CpuUsage = cpuUsage;
    }

    public long? TotalMemory { get; set; }
    public long? AvailableMemory { get; }
    public int? CpuUsage { get; }
}