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

    // 5 was a request of UDP packets that no client ever sent: a server before its removal reads it and fails, so the
    // number is not to be given to another request

    /// <summary>A rewarded ad the person has watched (<see cref="RewardedAdRequest"/>).</summary>
    RewardedAd = 10,

    /// <summary>A check that the server is there (<see cref="ServerCheckRequest"/>).</summary>
    ServerCheck = 30,

    /// <summary>The session's end (<see cref="ByeRequest"/>).</summary>
    Bye = 50
}