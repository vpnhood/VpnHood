using System.Net;
using VpnHood.Core.Packets;

namespace VpnHood.Core.TcpStack.Abstractions;

/// <summary>
/// Abstraction over a TCP stack (e.g. LocalTcpStack or LwipTcpStack).
/// Implementations receive raw IP packets
/// outbound IP packets via <see cref="OnPacketSend"/>. Accepted TCP connections are
/// exposed as <see cref="ITcpClient"/> objects through an <see cref="ITcpListener"/>.
/// </summary>
public interface ITcpStack : IDisposable
{
    /// <summary>
    /// Callback invoked when the stack needs to send a raw IP packet.
    /// The callback takes ownership of the packet and is responsible for disposing it.
    /// </summary>
    Action<IpPacket>? OnPacketSend { get; set; }

    /// <summary>
    /// Feeds a raw IP packet (as a byte span) into the stack for processing.
    /// The span is not retained after this call returns.
    /// </summary>
    void ProcessIncoming(ReadOnlySpan<byte> packetData);

    /// <summary>
    /// Feeds an already-parsed IP packet into the stack.
    /// The caller retains ownership of <paramref name="ipPacket"/>.
    /// </summary>
    void ProcessIncoming(IpPacket ipPacket);

    /// <summary>
    /// Creates a listener that accepts incoming TCP connections on any address and port.
    /// </summary>
    ITcpListener ListenAny();

    /// <summary>
    /// Creates a listener that accepts incoming TCP connections on the specified local endpoint.
    /// </summary>
    ITcpListener Listen(IPEndPoint localEndPoint);

    /// <summary>
    /// Aborts all active TCP connections immediately.
    /// </summary>
    void DropAllConnections();
}
