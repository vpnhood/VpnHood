using VpnHood.Core.Common.Utils;

namespace VpnHood.Core.Server.SystemInformation;

public class SystemInfo
{
    public required string OsInfo { get; init; }
    public required long? TotalMemory { get; init; }
    public required long? AvailableMemory { get; init; }
    public required int? CpuUsage { get; init; }
    public required int LogicalCoreCount { get; init; }

    public override string ToString()
    {
        var totalMemory = TotalMemory != null ? VhUtils.FormatBytes(TotalMemory.Value) : "*";
        return $"{OsInfo}, TotalMemory: {totalMemory}, Logical Cores: {LogicalCoreCount}";
    }
}