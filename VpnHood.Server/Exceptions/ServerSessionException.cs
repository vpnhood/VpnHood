using Microsoft.Extensions.Logging;
using System;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Tunneling;
using VpnHood.Common.Net;
using VpnHood.Tunneling.Messaging;

namespace VpnHood.Server.Exceptions;

public interface ILoggable
{
    public void Log();
}


public class ServerSessionException : SessionException, ILoggable
{
    public IPEndPointPair IpEndPointPair { get; }
    public Guid? TokenId { get; }
    public uint? SessionId { get; }
    public Guid? ClientId { get; }

    public ServerSessionException(
        IPEndPointPair ipEndPointPair,
        Session session,
        SessionErrorCode sessionErrorCode,
        string message)
        : base(sessionErrorCode, message)
    {
        IpEndPointPair = ipEndPointPair;
        SessionId = session.SessionId;
        TokenId = session.HelloRequest?.TokenId;
        ClientId = session.HelloRequest?.ClientInfo.ClientId;
    }

    public ServerSessionException(
        IPEndPointPair ipEndPointPair,
        Session session,
        SessionResponseBase sessionResponseBase)
        : base(sessionResponseBase)
    {
        IpEndPointPair = ipEndPointPair;
        SessionId = session.SessionId;
        TokenId = session.HelloRequest?.TokenId;
        ClientId = session.HelloRequest?.ClientInfo.ClientId;
    }


    public ServerSessionException(
        IPEndPointPair ipEndPointPair,
        SessionResponseBase sessionResponseBase,
        SessionRequest sessionRequest)
    : base(sessionResponseBase)
    {
        IpEndPointPair = ipEndPointPair;
        TokenId = sessionRequest.TokenId;
        ClientId = sessionRequest.ClientInfo.ClientId;
    }

    public ServerSessionException(
        IPEndPointPair ipEndPointPair,
        SessionResponseBase sessionResponseBase,
        RequestBase requestBase)
        : base(sessionResponseBase)
    {
        IpEndPointPair = ipEndPointPair;
        SessionId = requestBase.SessionId;
    }

    protected virtual LogLevel LogLevel => LogLevel.Information;
    protected virtual EventId EventId => SessionResponseBase.ErrorCode is SessionErrorCode.GeneralError
        ? GeneralEventId.Tcp
        : GeneralEventId.Session;

    public virtual void Log()
    {
        VhLogger.Instance.Log(LogLevel, EventId, this,
            "{Message}. SessionId: {SessionId}, ClientIp: {ClientIp}, TokenId: {TokenId}, SessionErrorCode: {SessionErrorCode}",
            Message, VhLogger.FormatSessionId(SessionId), VhLogger.Format(IpEndPointPair.RemoteEndPoint.Address),
            VhLogger.FormatId(TokenId), SessionResponseBase.ErrorCode);
    }
}