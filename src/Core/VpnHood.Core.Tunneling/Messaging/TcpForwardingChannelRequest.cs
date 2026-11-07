using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Tunneling.Messaging;

// Works like SOCKS's CONNECT inside the VPN session: the client has ended an app's TCP connection on the device and
// names where it goes, and the server opens a TCP connection of its own there; it opens nothing else, so the request
// is named for TCP. A StreamForwardingChannel then passes the bytes between the two.
public class TcpForwardingChannelRequest()
    : RequestBase(Messaging.RequestCode.TcpForwardingChannel)
{
    [JsonConverter(typeof(IPEndPointConverter))]
    public required IPEndPoint DestinationEndPoint { get; set; }
}