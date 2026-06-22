using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Quic.MsQuic;

/// <summary>
/// A <see cref="SystemSocketFactory"/> that adds MsQuic-based QUIC client support on platforms
/// where MsQuic is available (Windows/Linux). TCP/UDP creation is inherited from the base factory.
/// </summary>
public class MsQuicSocketFactory : SystemSocketFactory
{
    public override bool IsQuicSupported => MsQuicClient.IsSupported;

    public override IQuicClient CreateQuicClient() => MsQuicClient.IsSupported
        ? new MsQuicClient()
        : throw new NotSupportedException("QUIC is not supported on this platform.");
}
