
namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class ConnectionTimeoutException : TimeoutException
{
    public ConnectionTimeoutException(string message) : base(message)
    {
    }

    public ConnectionTimeoutException(string message, Exception innerException) : base(message, innerException)
    {
    }
}