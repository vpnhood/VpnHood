using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Tunneling.Forwarding;

public interface IPacketForwarderCallbacks
{
    public void OnConnectionRequested(IpProtocol protocolType, IpEndPointValue remoteEndPoint);

    public void OnConnectionEstablished(IpProtocol protocolType,
        IpEndPointValue localEndPoint, IpEndPointValue remoteEndPoint, 
        bool isNewLocalEndPoint, bool isNewRemoteEndPoint);
}