using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Core.Tunneling.Forwarding;

/// <summary>
/// Represents what hears of the destinations a forwarding manager's UDP and pings reach, as the server checks them and
/// logs them in its track log.
/// </summary>
public interface IPacketForwarderCallbacks
{
    /// <summary>Takes a new destination, before the first packet to it goes; a throw drops the packet.</summary>
    /// <param name="protocolType">The packets' protocol.</param>
    /// <param name="remoteEndPoint">The destination, with port 0 for a ping.</param>
    public void OnConnectionRequested(IpProtocol protocolType, IpEndPointValue remoteEndPoint);

    /// <summary>Takes a flow that has a new socket or a new destination, as its first packet goes.</summary>
    /// <param name="protocolType">The packets' protocol.</param>
    /// <param name="localEndPoint">
    /// The flow's local endpoint: the socket's for UDP, or the packet's source with port 0 for a ping.
    /// </param>
    /// <param name="remoteEndPoint">The destination, with port 0 for a ping.</param>
    /// <param name="isNewLocalEndPoint"><see langword="true"/> where the flow has a new socket.</param>
    /// <param name="isNewRemoteEndPoint"><see langword="true"/> where the destination is new.</param>
    public void OnConnectionEstablished(IpProtocol protocolType,
        IpEndPointValue localEndPoint, IpEndPointValue remoteEndPoint, 
        bool isNewLocalEndPoint, bool isNewRemoteEndPoint);
}