using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Tunneling.Messaging;

/// <summary>
/// Represents a request to forward one TCP connection: the server opens a TCP connection of its own to the destination,
/// and a <see cref="Channels.StreamForwardingChannel"/> passes the bytes between the two.
/// </summary>
/// <remarks>
/// It works like SOCKS's CONNECT inside the VPN session: the client has ended an app's TCP connection on the device and
/// names where it goes. The server opens nothing but TCP, so the request is named for it.
/// </remarks>
public class TcpForwardingChannelRequest()
    : RequestBase(Messaging.RequestCode.TcpForwardingChannel)
{
    /// <summary>Gets or sets the connection's destination.</summary>
    [JsonConverter(typeof(IPEndPointConverter))]
    public required IPEndPoint DestinationEndPoint { get; set; }
}