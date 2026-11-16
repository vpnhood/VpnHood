using System.Net;

namespace VpnHood.Core.Server.Abstractions;

/// <summary>
/// Represents what manages the server machine's network for the server: its interfaces' addresses and TCP's congestion
/// control, as the server's configuration asks.
/// </summary>
public interface INetConfigurationProvider
{
    /// <summary>Gets the names of the machine's network interfaces, as the provider lists them.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the names.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<string[]> GetInterfaceNames(CancellationToken cancellationToken);

    /// <summary>Adds an IP address to a network interface.</summary>
    /// <param name="ipAddress">The address.</param>
    /// <param name="interfaceName">The interface's name.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the address is added.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task AddIpAddress(IPAddress ipAddress, string interfaceName, CancellationToken cancellationToken);

    /// <summary>Removes an IP address from a network interface.</summary>
    /// <param name="ipAddress">The address.</param>
    /// <param name="interfaceName">The interface's name.</param>
    /// <remarks>It is synchronous, as the server's quick close calls it too.</remarks>
    void RemoveIpAddress(IPAddress ipAddress, string interfaceName);

    /// <summary>Gets whether an IP address is on one of the machine's network interfaces.</summary>
    /// <param name="ipAddress">The address.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is <see langword="true"/> where the address is there.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<bool> IpAddressExists(IPAddress ipAddress, CancellationToken cancellationToken);

    /// <summary>Gets the name of the TCP congestion control the machine uses, as <c>bbr</c> or <c>cubic</c>.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the name.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task<string> GetTcpCongestionControl(CancellationToken cancellationToken);

    /// <summary>
    /// Sets the TCP congestion control the machine uses; a provider may leave it as it is where the machine allows no
    /// change.
    /// </summary>
    /// <param name="value">The congestion control's name, as <c>bbr</c>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the provider is done.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task SetTcpCongestionControl(string value, CancellationToken cancellationToken);
}