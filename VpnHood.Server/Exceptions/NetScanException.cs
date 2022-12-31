using VpnHood.Common.Exceptions;
using VpnHood.Common.Messaging;

namespace VpnHood.Server.Exceptions;

internal class NetScanException : SessionException
{
    public NetScanException(uint sessionId)
        : base(SessionErrorCode.GeneralError, $"NetScan protector does not allow this request. SessionId: {sessionId}.")
    {
    }
}