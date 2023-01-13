using Microsoft.Extensions.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Tunneling;

namespace VpnHood.Server.Exceptions;

internal class NetScanException : ServerSessionException
{
    public NetScanException(IPEndPointPair ipEndPointPair, Session session)
        : base(ipEndPointPair, session, SessionErrorCode.GeneralError, "NetScan protector does not allow this request.")
    {
    }

    public override void Log()
    {
        //let EventReporter manage it
    }
    protected override LogLevel LogLevel => LogLevel.Warning;
    protected override EventId EventId => GeneralEventId.NetProtect;
}