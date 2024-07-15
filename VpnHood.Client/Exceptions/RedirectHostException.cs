using System.Net;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;

namespace VpnHood.Client.Exceptions;

public class RedirectHostException(SessionResponse sessionResponse) : SessionException(sessionResponse)
{
    public IPEndPoint[] RedirectHostEndPoints
    {
        get
        {
            if (!VhUtil.IsNullOrEmpty(SessionResponse.RedirectHostEndPoints))
                return SessionResponse.RedirectHostEndPoints;

            return SessionResponse.RedirectHostEndPoint != null
                ? [SessionResponse.RedirectHostEndPoint]
                : [];
        }
    }
}