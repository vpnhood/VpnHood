using System;
using System.Net;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Messaging;

namespace VpnHood.Client.Exceptions;

public class RedirectHostException : SessionException
{
    public RedirectHostException(SessionResponseBase sessionResponseBaseBase)
        : base(sessionResponseBaseBase)
    {
        if (sessionResponseBaseBase.RedirectHostEndPoint == null)
            throw new ArgumentNullException(nameof(sessionResponseBaseBase.RedirectHostEndPoint));
    }

    public IPEndPoint RedirectHostEndPoint => SessionResponseBase.RedirectHostEndPoint!;
}