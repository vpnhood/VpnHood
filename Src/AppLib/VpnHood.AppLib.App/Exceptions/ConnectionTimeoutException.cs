
namespace VpnHood.AppLib.Exceptions;

public class ConnectionTimeoutException : Exception
{
    public ConnectionTimeoutException(string message) : base(message)
    {
    }

    public ConnectionTimeoutException(string message, Exception innerException) : base(message, innerException)
    {
    }
}