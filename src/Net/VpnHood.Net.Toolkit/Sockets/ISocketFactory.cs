using System.Net;
using System.Net.Sockets;
using VpnHood.Net.Quic.Abstractions;

namespace VpnHood.Net.Toolkit.Sockets;

/// <summary>
/// Represents a factory of the sockets an app uses, so a platform or a decorator can protect, bind or configure each
/// one, such as to keep it out of a VPN's tunnel.
/// </summary>
public interface ISocketFactory
{
    /// <summary>Creates a TCP client for an endpoint's address family, not connected yet.</summary>
    /// <param name="ipEndPoint">The endpoint the client will connect to; its address family is the client's.</param>
    /// <param name="options">The client's options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The TCP client, which the caller owns and disposes.</returns>
    public TcpClient CreateTcpClient(IPEndPoint ipEndPoint, TcpClientOptions? options = null);

    /// <summary>Creates a UDP client of an address family.</summary>
    /// <param name="addressFamily">The address family.</param>
    /// <returns>The UDP client, which the caller owns and disposes.</returns>
    public UdpClient CreateUdpClient(AddressFamily addressFamily);

    /// <summary>
    /// Creates a UDP socket of an address family, for a worker that manages its own receive buffer.
    /// </summary>
    /// <remarks>
    /// A <see cref="UdpClient"/> keeps a receive buffer of its own, too costly for small-buffer workers such as DNS. The
    /// interface's default implementation returns the socket of <see cref="CreateUdpClient"/>, so a factory that does not
    /// implement it keeps its protection, binding and configuration.
    /// </remarks>
    /// <param name="addressFamily">The address family.</param>
    /// <returns>The socket, which the caller owns and disposes.</returns>
    // the discarded UdpClient wrapper has no finalizer, so the socket outlives it safely
    public Socket CreateUdpSocket(AddressFamily addressFamily) => CreateUdpClient(addressFamily).Client;

    /// <summary>Gets a value indicating whether the factory can create QUIC clients.</summary>
    /// <remarks>A decorator forwards it, and <see cref="CreateQuicClient"/>, to its inner factory.</remarks>
    public bool IsQuicSupported { get; }

    /// <summary>Creates a QUIC client.</summary>
    /// <returns>A new QUIC client.</returns>
    /// <exception cref="NotSupportedException">The factory cannot create QUIC clients.</exception>
    public IQuicClient CreateQuicClient();
}
