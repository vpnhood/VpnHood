using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Tunneling;

namespace VpnHood.Server.Exceptions;

internal class RequestBlockedException : ServerSessionException
{
    public RequestBlockedException(IPEndPoint remoteEndPoint, Session session)
        : base(remoteEndPoint, session, SessionErrorCode.GeneralError, "The destination address is blocked.")
    {
    }

    protected override LogLevel LogLevel => LogLevel.Information;
    protected override EventId EventId => GeneralEventId.NetFilter;
    
    public override void Log()
    {
        //let EventReporter manage it
    }
}