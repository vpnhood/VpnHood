using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Toolkit.Sockets;

namespace VpnHood.Core.Quic.Ios;

/// <summary>
/// A <see cref="SystemSocketFactory"/> that adds iOS (Network.framework) QUIC client support.
/// TCP/UDP creation is inherited from the base factory.
/// </summary>
public class IosSocketFactory : SystemSocketFactory
{
    public override bool IsQuicSupported => IosQuicClient.IsSupported;

    public override IQuicClient CreateQuicClient() => IosQuicClient.IsSupported
        ? new IosQuicClient()
        : throw new NotSupportedException("QUIC is not supported on this platform.");
}
