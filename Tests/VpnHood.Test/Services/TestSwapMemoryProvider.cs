using VpnHood.Server.Abstractions;

namespace VpnHood.Test.Services;

public class TestSwapMemoryProvider : ISwapMemoryProvider
{
    public long AppSize { get; set; } 
    public long AppUsed { get; set; }
    public long OtherSize { get; set; }
    public long OtherUsed { get; set; }

    public SwapMemoryInfo Info => new() {
        AppSize = AppSize,
        AppUsed = AppUsed,
        TotalSize = OtherSize + AppSize,
        TotalUsed = OtherUsed + AppUsed
    };

    public Task<SwapMemoryInfo> GetInfo()
    {
        return Task.FromResult(Info);
    }

    public Task SetAppSwapMemorySize(long size)
    {
        AppSize = size;
        return Task.CompletedTask;
    }
}