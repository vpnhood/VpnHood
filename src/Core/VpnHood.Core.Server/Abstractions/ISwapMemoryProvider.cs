namespace VpnHood.Core.Server.Abstractions;

public interface ISwapMemoryProvider
{
    Task<SwapMemoryInfo> GetInfo(CancellationToken cancellationToken);
    Task SetAppSwapMemorySize(long size, CancellationToken cancellationToken);
}