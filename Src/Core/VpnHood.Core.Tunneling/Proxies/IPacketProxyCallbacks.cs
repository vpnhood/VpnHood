using System.Net;
using VpnHood.Core.Toolkit.Net;

namespace VpnHood.Core.Tunneling.Proxies;

public interface IPacketProxyCallbacks
{
    public void OnConnectionRequested(IpProtocol protocolType, IpEndPointValue remoteEndPoint);

    public void OnConnectionEstablished(IpProtocol protocolType,
        IpEndPointValue localEndPoint, IpEndPointValue remoteEndPoint, 
        bool isNewLocalEndPoint, bool isNewRemoteEndPoint);
}