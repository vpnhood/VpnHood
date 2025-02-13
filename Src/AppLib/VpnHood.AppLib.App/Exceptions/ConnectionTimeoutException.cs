using VpnHood.Core.Common.Exceptions;

namespace VpnHood.AppLib.Exceptions;

public class ConnectionTimeoutException : LoadAdException
{
    public ConnectionTimeoutException(string message) : base(message)
    {
    }

    public ConnectionTimeoutException(string message, Exception innerException) : base(message, innerException)
    {
    }
}