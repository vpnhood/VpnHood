using VpnHood.Core.Client;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.Test;

public static class VpnHoodClientExtensions
{
    public static ISessionStatus GetSessionStatus(this VpnHoodClient client)
        => client.SessionStatus ??
           throw new InvalidOperationException("Session has not been initialized yet.");

    public static SessionErrorCode GetLastSessionErrorCode(this VpnHoodClient client)
        => (client.LastException as SessionException)?.SessionResponse.ErrorCode ?? SessionErrorCode.Ok;

}