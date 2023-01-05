using VpnHood.Common.Exceptions;
using VpnHood.Common.Messaging;

namespace VpnHood.Server.Exceptions;

internal class MaxTcpConnectWaitException : SessionException
{
    public MaxTcpConnectWaitException(uint sessionId)
        : base(SessionErrorCode.GeneralError, $"Maximum TcpConnectWait has been reached. SessionId: {sessionId}.")
    {
    }
}