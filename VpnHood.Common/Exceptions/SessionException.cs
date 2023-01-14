using System;
using VpnHood.Common.Messaging;

namespace VpnHood.Common.Exceptions;

public class SessionException : Exception
{
    public SessionException(SessionResponseBase sessionResponseBase)
        : base(sessionResponseBase.ErrorMessage)
    {
        SessionResponseBase = sessionResponseBase;
    }

    public SessionException(SessionErrorCode errorCode, string? message = null) : base(message)
    {
        SessionResponseBase = new SessionResponseBase(errorCode) { ErrorMessage = message };
    }

    public SessionResponseBase SessionResponseBase { get; }
}