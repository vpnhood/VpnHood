using VpnHood.Common.Messaging;
using VpnHood.Server.Access.Messaging;

namespace VpnHood.AccessServer.Agent.Exceptions;

public sealed class SessionExceptionEx(SessionResponseEx response)
    : Exception(response.ErrorMessage)
{
    public SessionExceptionEx(SessionErrorCode errorCode, string message)
        : this(new SessionResponseEx {
            ErrorCode = errorCode,
            ErrorMessage = message
        })
    {
    }

    public SessionResponseEx SessionResponseEx => response;
}