namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class ConnectionTimeoutException(string message, Exception? innerException = null)
    : TimeoutException(message, innerException);
