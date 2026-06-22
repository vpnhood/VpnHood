using VpnHood.Core.Quic.Abstractions;
using VpnHood.Core.Toolkit.Sockets;

namespace VpnHood.Core.Quic.Droid;

/// <summary>
/// A <see cref="SystemSocketFactory"/> that adds Android QUIC client support via <see cref="AndroidQuicClient"/>
/// (our own MsQuic P/Invoke binding over the bundled libmsquic.so). This deliberately does NOT use
/// System.Net.Quic, whose TLS certificate validation is incompatible with Android's crypto backend.
/// TCP/UDP creation is inherited from the base factory.
/// </summary>
public class AndroidSocketFactory : SystemSocketFactory
{
    public override bool IsQuicSupported => AndroidQuicClient.IsSupported;

    public override IQuicClient CreateQuicClient() => AndroidQuicClient.IsSupported
        ? new AndroidQuicClient()
        : throw new NotSupportedException("QUIC is not supported on this platform.");
}
