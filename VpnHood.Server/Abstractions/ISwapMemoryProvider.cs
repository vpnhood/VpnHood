namespace VpnHood.Server.Abstractions;

public interface ISwapMemoryProvider
{
    Task<SwapMemoryInfo> GetInfo();
    Task SetAppSwapMemorySize(long size);
}