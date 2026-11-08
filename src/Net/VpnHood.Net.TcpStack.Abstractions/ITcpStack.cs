using System.Net;
using VpnHood.Net.Packets;

namespace VpnHood.Net.TcpStack.Abstractions;

/// <summary>
/// Represents a TCP stack that terminates the TCP connections carried in IP packets: it is fed packets through
/// <see cref="ProcessIncoming"/>, hands the packets it produces to <see cref="OnPacketSend"/>, and gives each
/// accepted connection as an <see cref="ITcpClient"/> through an <see cref="ITcpListener"/>.
/// </summary>
public interface ITcpStack : IDisposable
{
    /// <summary>
    /// Gets or sets the callback that sends the IP packets the stack produces.
    /// </summary>
    /// <remarks>
    /// The callback owns each packet it returns from and disposes it once sent. When the callback throws, the stack
    /// disposes the packet, so the callback must not keep it; with no callback, the stack disposes its packets. The
    /// callback runs inline on whichever thread drives the stack: packet processing, a stream's read or write, or the
    /// stack's timers, so calls may overlap.
    /// </remarks>
    Action<IpPacket>? OnPacketSend { get; set; }

    /// <summary>
    /// Processes an IP packet received for the stack.
    /// </summary>
    /// <remarks>
    /// The stack copies what it keeps: the caller still owns the packet and disposes it. A packet that is not TCP is
    /// ignored.
    /// </remarks>
    /// <param name="ipPacket">The packet, already parsed.</param>
    void ProcessIncoming(IpPacket ipPacket);

    /// <summary>
    /// Gets the listener that accepts the connections no endpoint listener takes, to any address and port.
    /// </summary>
    /// <returns>The listener; each call returns the same one until it is stopped.</returns>
    /// <exception cref="ObjectDisposedException">The stack is disposed.</exception>
    ITcpListener ListenAny();

    /// <summary>
    /// Gets the listener that accepts the connections to an endpoint: that address and that port exactly.
    /// </summary>
    /// <param name="localEndPoint">The endpoint the connections are made to.</param>
    /// <returns>The listener; each call for the same endpoint returns the same one until it is stopped.</returns>
    /// <exception cref="ObjectDisposedException">The stack is disposed.</exception>
    ITcpListener Listen(IPEndPoint localEndPoint);

    /// <summary>
    /// Aborts every connection: each peer gets a reset, and each stream reads as ended. Listeners stay, and new
    /// connections are accepted afterward.
    /// </summary>
    void DropAllConnections();
}
