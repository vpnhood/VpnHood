using System;

namespace VpnHood.Server.Exceptions;

internal class TlsAuthenticateException : Exception
{
    public TlsAuthenticateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}