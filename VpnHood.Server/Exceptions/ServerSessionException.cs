using Microsoft.Extensions.Logging;
using System;
using System.Net;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Messaging;

namespace VpnHood.Server.Exceptions;

public class ServerSessionException : SessionException, ISelfLog
{
    public IPEndPoint RemoteEndPoint { get; }
    public Guid? TokenId { get; }
    public uint? SessionId { get; set; }
    public Session? Session { get; }
    public Guid? ClientId { get; }

    public ServerSessionException(
        IPEndPoint remoteEndPoint,
        Session session,
        SessionErrorCode sessionErrorCode,
        string message)
        : base(sessionErrorCode, message)
    {
        RemoteEndPoint = remoteEndPoint;
        SessionId = session.SessionId;
        TokenId = session.HelloRequest?.TokenId;
        ClientId = session.HelloRequest?.ClientInfo.ClientId;
        Session = session;
    }

    public ServerSessionException(
        IPEndPoint remoteEndPoint,
        Session session,
        SessionResponseBase sessionResponseBase)
        : base(sessionResponseBase)
    {
        RemoteEndPoint = remoteEndPoint;
        TokenId = session.HelloRequest?.TokenId;
        ClientId = session.HelloRequest?.ClientInfo.ClientId;
        SessionId = session.SessionId;
        Session = session;

    }

    public ServerSessionException(
        IPEndPoint remoteEndPoint,
        SessionResponseBase sessionResponseBase,
        SessionRequest sessionRequest)
    : base(sessionResponseBase)
    {
        RemoteEndPoint = remoteEndPoint;
        TokenId = sessionRequest.TokenId;
        ClientId = sessionRequest.ClientInfo.ClientId;
    }

    public ServerSessionException(
        IPEndPoint remoteEndPoint,
        SessionResponseBase sessionResponseBase,
        RequestBase requestBase)
        : base(sessionResponseBase)
    {
        RemoteEndPoint = remoteEndPoint;
        SessionId = requestBase.SessionId;
    }

    protected virtual LogLevel LogLevel => LogLevel.Information;
    protected virtual EventId EventId => SessionResponseBase.ErrorCode is SessionErrorCode.GeneralError
        ? GeneralEventId.Tcp
        : GeneralEventId.Session;

    public virtual void Log()
    {
        VhLogger.Instance.Log(LogLevel, EventId, this,
            "{Message} SessionId: {SessionId}, ClientIp: {ClientIp}, TokenId: {TokenId}, SessionErrorCode: {SessionErrorCode}",
            Message, VhLogger.FormatSessionId(SessionId), VhLogger.Format(RemoteEndPoint.Address),
            VhLogger.FormatId(TokenId), SessionResponseBase.ErrorCode);
    }
}