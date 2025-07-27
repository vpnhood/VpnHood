namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class EndPointDiscoveryException : TimeoutException
{
    public EndPointDiscoveryException(string message) : base(message)
    {
    }

    public EndPointDiscoveryException(string message, Exception innerException) : base(message, innerException)
    {
    }
}