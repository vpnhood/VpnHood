namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class EndPointDiscoveryException(string message, Exception? innerException = null)
    : TimeoutException(message, innerException);
