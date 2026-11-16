namespace VpnHood.Core.Server.Abstractions;

/// <summary>
/// Represents what manages the server machine's swap memory: the machine's own, and the swap file the server adds, as
/// its configuration asks.
/// </summary>
public interface ISwapMemoryProvider
{
    /// <summary>Gets the machine's swap memory now.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the swap memory's sizes.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<SwapMemoryInfo> GetInfo(CancellationToken cancellationToken);

    /// <summary>Sets the size of the server's own swap file, which a provider may round, or take as none when small.</summary>
    /// <param name="size">The size, in bytes; 0 for none.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the provider is done.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task SetAppSwapMemorySize(long size, CancellationToken cancellationToken);
}