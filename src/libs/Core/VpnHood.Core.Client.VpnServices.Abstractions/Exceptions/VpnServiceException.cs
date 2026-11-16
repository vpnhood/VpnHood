namespace VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;

/// <summary>
/// The exception that is thrown when the VPN service fails, as when it ends without a connection.
/// </summary>
/// <param name="message">The message.</param>
/// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
public class VpnServiceException(string message, Exception? innerException = null)
    : Exception(message, innerException);
