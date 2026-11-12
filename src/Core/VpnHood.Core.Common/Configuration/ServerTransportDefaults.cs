using System.Net;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Common.Configuration;

/// <summary>
/// Provides the transport defaults the server alone applies, where its configuration, which comes from the access
/// manager, gives none.
/// </summary>
/// <remarks>
/// The settable ones are the process's, read when the server needs them. A null buffer size leaves the socket at the
/// system's default.
/// </remarks>
// the counterpart of ClientTransportOptions, kept static as the values it falls back to arrive over the wire
public static class ServerTransportDefaults
{
    /// <summary>Gets the buffer sizes of a TCP connection the server forwards: about 8 KB (8,191 bytes) each way.</summary>
    public static TransferBufferSize StreamForwardingBufferSize { get; } =
        new(0xFFFF / 8, 0xFFFF / 8); // 8KB send, 8KB receive

    /// <summary>Gets the buffer sizes of a stream packet channel: about 16 KB (16,383 bytes) each way.</summary>
    public static TransferBufferSize StreamPacketBufferSize { get; } =
        new(0xFFFF / 4, 0xFFFF / 4); // 16KB send, 16KB receive

    /// <summary>Gets or sets the buffer sizes of the sockets of forwarded UDP, or <see langword="null"/> for the system's.</summary>
    public static TransferBufferSize? UdpForwardingBufferSize { get; set; }

    /// <summary>Gets or sets the buffer sizes of the UDP channel's socket, or <see langword="null"/> for the system's.</summary>
    public static TransferBufferSize? UdpChannelBufferSize { get; set; }

    /// <summary>
    /// Gets or sets the kernel buffer sizes of the TCP sockets the server accepts, or <see langword="null"/> for the
    /// system's.
    /// </summary>
    public static TransferBufferSize? TcpKernelBufferSize { get; set; }

    /// <summary>
    /// Gets the IPv4 network the server gives its clients' virtual addresses from, <c>10.240.0.0/12</c>; a client is
    /// told its address in the session's hello.
    /// </summary>
    public static IpNetwork VirtualIpNetworkV4 { get; } = new(IPAddress.Parse("10.240.0.1"), 12); //1M (enough for reservation)

    /// <summary>Gets the IPv6 network the server gives its clients' virtual addresses from, <c>fd12:2020::/48</c>.</summary>
    public static IpNetwork VirtualIpNetworkV6 { get; } = new(IPAddress.Parse("fd12:2020::1"), 48);

    /// <summary>
    /// Gets or sets the margin the server gives a client's timeouts in the session's hello: the client waits for a
    /// request this much longer than the server's TCP connect timeout, and closes an idle channel this much sooner than
    /// the server's idle timeout. The default is 10 seconds.
    /// </summary>
    public static TimeSpan ClientRequestTimeoutDelta { get; set; } = TimeSpan.FromSeconds(10);
}
