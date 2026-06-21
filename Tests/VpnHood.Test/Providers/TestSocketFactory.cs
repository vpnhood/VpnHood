using VpnHood.Core.Quic.MsQuic;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Test.Providers;

// Wraps SystemSocketFactory with MsQuic so QUIC-channel tests work on platforms where MsQuic is available.
public class TestSocketFactory() : MsQuicSocketFactory(new SystemSocketFactory());
