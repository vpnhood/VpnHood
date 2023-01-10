using Microsoft.Extensions.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Tunneling;

namespace VpnHood.Server.Exceptions;

internal class MaxTcpConnectWaitException : ServerSessionException
{
    public MaxTcpConnectWaitException(IPEndPointPair ipEndPointPair, Session session)
        : base(ipEndPointPair, session, SessionErrorCode.GeneralError, "Maximum TcpConnectWait has been reached.")
    {
    }

    protected override LogLevel LogLevel => LogLevel.Warning;
    protected override EventId EventId => GeneralEventId.NetProtect;

}