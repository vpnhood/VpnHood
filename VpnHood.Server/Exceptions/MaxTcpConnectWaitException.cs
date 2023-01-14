using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Tunneling;

namespace VpnHood.Server.Exceptions;

internal class MaxTcpConnectWaitException : ServerSessionException
{
    public MaxTcpConnectWaitException(IPEndPoint remoteEndPoint, Session session)
        : base(remoteEndPoint, session, SessionErrorCode.GeneralError, "Maximum TcpConnectWait has been reached.")
    {
    }

    public override void Log()
    {
        //let EventReporter manage it
    }

    protected override LogLevel LogLevel => LogLevel.Warning;
    protected override EventId EventId => GeneralEventId.NetProtect;

}