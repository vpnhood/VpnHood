using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Tunneling;

namespace VpnHood.Core.Server.Exceptions;

internal class MaxTcpChannelException(IPEndPoint remoteEndPoint, Session session, string requestId)
    : ServerSessionException(remoteEndPoint, session, SessionErrorCode.GeneralError, requestId,
        "Maximum TcpChannel has been reached.")
{
    protected override LogLevel LogLevel => LogLevel.Warning;
    protected override EventId EventId => GeneralEventId.NetProtect;

    public override void Log()
    {
        //let EventReporter manage it
    }
}