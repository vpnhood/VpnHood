using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Tunneling;

namespace VpnHood.Core.Server.Exceptions;

internal class RequestBlockedException(IPEndPoint remoteEndPoint, Session session, string requestId)
    : ServerSessionException(remoteEndPoint, session, SessionErrorCode.GeneralError, requestId,
        $"The destination address is blocked. Address: {VhLogger.Format(remoteEndPoint)}")
{
    protected override LogLevel LogLevel => LogLevel.Information;
    protected override EventId EventId => GeneralEventId.NetFilter;

    public override void Log()
    {
        //let EventReporter manage it
    }
}