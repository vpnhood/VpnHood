using VpnHood.Common.Exceptions;
using VpnHood.Common.Messaging;

namespace VpnHood.Server.Exceptions;

internal class MaxTcpChannelException : SessionException
{
    public MaxTcpChannelException(uint sessionId)
        : base(SessionErrorCode.GeneralError, $"Maximum TcpChannel has been reached. SessionId: {sessionId}.")
    {
    }
}