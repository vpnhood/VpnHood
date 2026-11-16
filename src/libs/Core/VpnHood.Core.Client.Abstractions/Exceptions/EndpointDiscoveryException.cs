namespace VpnHood.Core.Client.Abstractions.Exceptions;

/// <summary>
/// The exception that is thrown when no endpoint of the servers can be found from the server token.
/// </summary>
/// <param name="message">The message.</param>
/// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
public class EndPointDiscoveryException(string message, Exception? innerException = null)
    : TimeoutException(message, innerException);
