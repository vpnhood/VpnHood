namespace VpnHood.Core.Tunneling.Messaging;

/// <summary>Specifies the kind of a client's request.</summary>
/// <remarks>
/// A request on a stream is a byte of its version, 1, a byte of this code, then the request as JSON, with its length
/// in four bytes before it.
/// </remarks>
public enum RequestCode : byte
{
    /// <summary>A request for a session (<see cref="HelloRequest"/>).</summary>
    Hello = 1,

    /// <summary>A stream that carries the session's packets (<see cref="TcpPacketChannelRequest"/>).</summary>
    TcpPacketChannel = 2,

    /// <summary>A stream that carries one forwarded TCP connection (<see cref="TcpForwardingChannelRequest"/>).</summary>
    TcpForwardingChannel = 3,

    /// <summary>The session's status (<see cref="SessionStatusRequest"/>).</summary>
    SessionStatus = 4,

    /// <summary>
    /// UDP packets in a request of their own (<see cref="UdpPacketRequest"/>), which no client sends and the server does
    /// not serve.
    /// </summary>
    UdpPacket = 5,

    /// <summary>A rewarded ad the person has watched (<see cref="RewardedAdRequest"/>).</summary>
    RewardedAd = 10,

    /// <summary>A check that the server is there (<see cref="ServerCheckRequest"/>).</summary>
    ServerCheck = 30,

    /// <summary>The session's end (<see cref="ByeRequest"/>).</summary>
    Bye = 50
}