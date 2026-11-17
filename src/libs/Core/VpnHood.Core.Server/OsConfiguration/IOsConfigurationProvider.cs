using System.Net;

namespace VpnHood.Core.Server.OsConfiguration;

/// <summary>
/// Represents the server machine's operating system, as the server reads and changes it.
/// </summary>
/// <remarks>
/// Each member does one thing or throws: <see cref="NotSupportedException"/> where the machine lacks what it does, such
/// as a container's kernel settings, and any other exception where it fails. What to change, when, and what a failure
/// means is the server's, so an implementation keeps no defaults, no state and no log.
/// </remarks>
public interface IOsConfigurationProvider
{
    /// <summary>Gets the names of the machine's network interfaces, in the machine's order, without its loopback.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the names.</returns>
    /// <exception cref="NotSupportedException">The machine does not list them.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<IReadOnlyList<string>> GetInterfaceNames(CancellationToken cancellationToken);

    /// <summary>Gets whether an IP address is on one of the machine's network interfaces.</summary>
    /// <param name="ipAddress">The address.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is <see langword="true"/> if the address is there; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="NotSupportedException">The machine does not tell.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<bool> IpAddressExists(IPAddress ipAddress, CancellationToken cancellationToken);

    /// <summary>Adds an IP address to a network interface.</summary>
    /// <param name="ipAddress">The address.</param>
    /// <param name="interfaceName">The interface's name.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the address is added.</returns>
    /// <exception cref="ArgumentException"><paramref name="interfaceName"/> is no interface name.</exception>
    /// <exception cref="NotSupportedException">The machine takes no address from the server.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task AddIpAddress(IPAddress ipAddress, string interfaceName, CancellationToken cancellationToken);

    /// <summary>Removes an IP address from a network interface.</summary>
    /// <param name="ipAddress">The address.</param>
    /// <param name="interfaceName">The interface's name.</param>
    /// <remarks>It is synchronous, as the server's quick close calls it too.</remarks>
    /// <exception cref="ArgumentException"><paramref name="interfaceName"/> is no interface name.</exception>
    /// <exception cref="NotSupportedException">The machine takes no change from the server.</exception>
    void RemoveIpAddress(IPAddress ipAddress, string interfaceName);

    /// <summary>Gets the name of the TCP congestion control the machine uses, such as <c>bbr</c> or <c>cubic</c>.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the name.</returns>
    /// <exception cref="NotSupportedException">The machine does not tell.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<string> GetTcpCongestionControl(CancellationToken cancellationToken);

    /// <summary>Sets the TCP congestion control the machine uses.</summary>
    /// <param name="name">The congestion control's name, such as <c>bbr</c>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once it is set.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is no congestion control's name.</exception>
    /// <exception cref="NotSupportedException">
    /// The machine has no such congestion control, or takes no change from the server.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task SetTcpCongestionControl(string name, CancellationToken cancellationToken);

    /// <summary>Gets the most connections the machine tracks at once, as a NAT needs.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the number.</returns>
    /// <exception cref="NotSupportedException">The machine tracks no connections.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<int> GetConnectionTrackingMax(CancellationToken cancellationToken);

    /// <summary>Sets the most connections the machine tracks at once.</summary>
    /// <param name="max">The number.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once it is set.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> is 0 or less.</exception>
    /// <exception cref="NotSupportedException">
    /// The machine tracks no connections, or takes no change from the server.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task SetConnectionTrackingMax(int max, CancellationToken cancellationToken);

    /// <summary>
    /// Gets how long the machine keeps tracking an established TCP connection that sends nothing.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the time.</returns>
    /// <exception cref="NotSupportedException">The machine tracks no connections.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<TimeSpan> GetConnectionTrackingTcpIdleTimeout(CancellationToken cancellationToken);

    /// <summary>
    /// Sets how long the machine keeps tracking an established TCP connection that sends nothing.
    /// </summary>
    /// <param name="timeout">The time, in whole seconds.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once it is set.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout"/> is under a second, or over what the machine takes.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The machine tracks no connections, or takes no change from the server.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task SetConnectionTrackingTcpIdleTimeout(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Gets the machine's swap memory now, the server's own swap file with it.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the swap memory's sizes.</returns>
    /// <exception cref="NotSupportedException">The machine does not tell.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<SwapMemoryInfo> GetSwapMemoryInfo(CancellationToken cancellationToken);

    /// <summary>
    /// Sets the size of the server's own swap file, which an implementation may round, or take as none when small.
    /// </summary>
    /// <param name="size">The size, in bytes; 0 for none.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the file has its size.</returns>
    /// <exception cref="NotSupportedException">The machine takes no swap file from the server.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task SetAppSwapMemorySize(long size, CancellationToken cancellationToken);
}
