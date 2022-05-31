using System;
using System.Net;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Messaging;

namespace VpnHood.Client.Exceptions;

public class RedirectHostException : SessionException
{
    public RedirectHostException(ResponseBase responseBase)
        : base(responseBase)
    {
        if (responseBase.RedirectHostEndPoint == null)
            throw new ArgumentNullException(nameof(responseBase.RedirectHostEndPoint));
    }

    public IPEndPoint RedirectHostEndPoint => SessionResponse.RedirectHostEndPoint!;
}