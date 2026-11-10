using VpnHood.Core.Client;
using VpnHood.Net.Quic.Abstractions;
using VpnHood.Net.Quic.MsQuic;
using VpnHood.Net.Toolkit.Sockets;

namespace VpnHood.Test.Providers;

// The system's sockets and MsQuic's QUIC in one object, so a test hands one factory for both and a factory derived
// from it watches both
public class TestSocketFactory : SystemSocketFactory, IClientSocketFactory, IQuicClientFactory
{
    private readonly MsQuicClientFactory _quicClientFactory = new();

    public IQuicClientFactory QuicClientFactory => this;
    public virtual bool IsSupported => _quicClientFactory.IsSupported;
    public virtual IQuicClient CreateClient() => _quicClientFactory.CreateClient();
}
