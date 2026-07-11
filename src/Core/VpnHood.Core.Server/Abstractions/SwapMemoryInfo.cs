namespace VpnHood.Core.Server.Abstractions;

public class SwapMemoryInfo
{
    public required long TotalSize { get; init; }
    public required long TotalUsed { get; init; }
    public required long AppSize { get; init; }
    public required long AppUsed { get; init; }
}