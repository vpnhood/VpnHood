using System.Net;

namespace VpnHood.Core.Tunneling.Channels;

/// <summary>
/// Represents where a session's datagrams go: the socket and the address, swapped as one, so a send never pairs one
/// route's socket with another's address.
/// </summary>
/// <param name="Transmitter">The transmitter whose socket sends the datagrams.</param>
/// <param name="EndPoint">The endpoint the datagrams go to.</param>
public sealed record UdpRoute(UdpChannelTransmitter Transmitter, IPEndPoint EndPoint);
