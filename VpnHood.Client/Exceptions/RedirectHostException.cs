using System.Net;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Messaging;

namespace VpnHood.Client.Exceptions;

public class RedirectHostException : SessionException
{
    public RedirectHostException(SessionResponse sessionResponse)
        : base(sessionResponse)
    {
        if (sessionResponse.RedirectHostEndPoint == null)
            throw new ArgumentNullException(nameof(sessionResponse.RedirectHostEndPoint));
    }

    public IPEndPoint RedirectHostEndPoint => SessionResponse.RedirectHostEndPoint!;
}