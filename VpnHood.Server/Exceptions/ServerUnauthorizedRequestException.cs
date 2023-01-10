using Microsoft.Extensions.Logging;
using System;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Tunneling.Messaging;

namespace VpnHood.Server.Exceptions;

public class ServerUnauthorizedAccessException : UnauthorizedAccessException
{
    public IPEndPointPair IpEndPointPair { get; }
    public Guid? TokenId { get; }
    public uint? SessionId { get; }
    public Guid? ClientId { get; }

    public ServerUnauthorizedAccessException(string message, IPEndPointPair ipEndPointPair, HelloRequest helloRequest)
        : base(message)
    {
        IpEndPointPair = ipEndPointPair;
        TokenId = helloRequest.TokenId;
        ClientId = helloRequest.ClientInfo.ClientId;
    }

    public ServerUnauthorizedAccessException(string message, IPEndPointPair ipEndPointPair, uint sessionId)
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
        TokenId = session.HelloRequest?.TokenId;
        ClientId = session.HelloRequest?.ClientInfo.ClientId;
    }

    public virtual void Log()
    {
        VhLogger.Instance.LogInformation("{Message}. SessionId: {SessionId}, ClientIp: {ClientIp}, TokenId: {TokenId}", 
            Message, VhLogger.FormatSessionId(SessionId), VhLogger.Format(IpEndPointPair.RemoteEndPoint.Address), VhLogger.FormatId(TokenId));
    }
}
