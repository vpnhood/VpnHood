using System.Net;

namespace VpnHood.Core.Tunneling.Channels;

// where a session's datagrams go: the socket and the address, swapped as one, so a send never pairs one route's socket
// with another's address
public sealed record UdpRoute(UdpChannelTransmitter Transmitter, IPEndPoint EndPoint);
