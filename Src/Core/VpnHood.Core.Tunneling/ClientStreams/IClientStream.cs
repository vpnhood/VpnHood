using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Tunneling.ClientStreams;

public interface IClientStream : IDisposable
{
    string ClientStreamId { get; set; }
    bool RequireHttpResponse { get; set; }
    IPEndPointPair IpEndPointPair { get; }
    Stream Stream { get; }
    bool Connected { get; }
    void PreventReuse();
}