namespace VpnHood.Core.Client.Abstractions.Exceptions;

/// <summary>
/// The exception that is thrown when a connection to the server takes too long.
/// </summary>
/// <param name="message">The message.</param>
/// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
public class ConnectionTimeoutException(string message, Exception? innerException = null)
    : TimeoutException(message, innerException);
