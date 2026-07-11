using System.Net;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client.Exceptions;

public class RedirectHostException(SessionResponse sessionResponse)
    : SessionException(sessionResponse)
{
    public ServerToken[]? RedirectServerTokens => SessionResponse.RedirectServerTokens;

    [Obsolete("Deprecated on protocol 10. User RedirectServerTokens")]
    public IPEndPoint[] RedirectHostEndPoints {
        get {
            if (!VhUtils.IsNullOrEmpty(SessionResponse.RedirectHostEndPoints))
                return SessionResponse.RedirectHostEndPoints;

            return SessionResponse.RedirectHostEndPoint != null
                ? [SessionResponse.RedirectHostEndPoint]
                : [];
        }
    }
}