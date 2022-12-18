namespace VpnHood.Server.SystemInformation;

public class SystemInfo
{
    public SystemInfo(long? totalMemory, long? freeMemory, int? cpuUsage)
    {
        TotalMemory = totalMemory;
        FreeMemory = freeMemory;
        CpuUsage = cpuUsage;
    }

    public long? TotalMemory { get; set; }
    public long? FreeMemory { get; }
    public int? CpuUsage { get; }
}