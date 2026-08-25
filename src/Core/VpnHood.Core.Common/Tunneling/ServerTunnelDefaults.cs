using System.Net;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Common.Tunneling;

/// <summary>
/// Tunnel defaults only the server applies. They live here, not in <c>TunnelDefaults</c>, so the
/// client cannot reach them: nothing outside this project has any business reading them, and a
/// shared class made that a matter of discipline rather than of compilation.
/// <para>A null buffer size means "leave the socket at the system default".</para>
/// </summary>
public static class ServerTunnelDefaults
{
    public static TransferBufferSize StreamProxyBufferSize { get; } =
        new(0xFFFF / 8, 0xFFFF / 8); // 8KB send, 8KB receive

    public static TransferBufferSize StreamPacketBufferSize { get; } =
        new(0xFFFF / 4, 0xFFFF / 4); // 16KB send, 16KB receive

    public static TransferBufferSize? UdpProxyBufferSize { get; set; }
    public static TransferBufferSize? UdpChannelBufferSize { get; set; }
    public static TransferBufferSize? TcpKernelBufferSize { get; set; }

    // The address pools the server hands out; a client never picks its own, it is told which
    // virtual IPs it got in the session hello.
    public static IpNetwork VirtualIpNetworkV4 { get; } = new(IPAddress.Parse("10.240.0.1"), 12); //1M (enough for reservation)
    public static IpNetwork VirtualIpNetworkV6 { get; } = new(IPAddress.Parse("fd12:2020::1"), 48);

    // How much longer than the client's own request timeout the server waits before giving up on
    // it — named for the client only because that is whose timeout it pads.
    public static TimeSpan ClientRequestTimeoutDelta { get; set; } = TimeSpan.FromSeconds(10);
}
