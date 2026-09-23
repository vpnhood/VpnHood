using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Tunneling.Messaging;

namespace VpnHood.Core.Server.Exceptions;

public class ServerUnauthorizedAccessException : UnauthorizedAccessException
{
    public IPEndPointPair IpEndPointPair { get; }
    public string? TokenId { get; }
    public ulong? SessionId { get; }
    public string? ClientId { get; }

    public ServerUnauthorizedAccessException(string message, IPEndPointPair ipEndPointPair, HelloRequest helloRequest)
        : base(message)
    {
        IpEndPointPair = ipEndPointPair;
        TokenId = helloRequest.TokenId;
        ClientId = helloRequest.ClientInfo.ClientId;
    }

    public ServerUnauthorizedAccessException(string message, IPEndPointPair ipEndPointPair, ulong sessionId)
        : base(message)
    {
        IpEndPointPair = ipEndPointPair;
        SessionId = sessionId;
    }

    public ServerUnauthorizedAccessException(string message, IPEndPointPair ipEndPointPair, Session session)
        : base(message)
    {
        IpEndPointPair = ipEndPointPair;
        SessionId = session.SessionId;
    }
}