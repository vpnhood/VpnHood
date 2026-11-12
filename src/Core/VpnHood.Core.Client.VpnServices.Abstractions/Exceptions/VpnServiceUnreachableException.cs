namespace VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;

/// <summary>
/// The exception that is thrown when the VPN service cannot be reached, or does not answer.
/// </summary>
/// <param name="message">The message.</param>
/// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
public class VpnServiceUnreachableException(string message, Exception? innerException = null)
    : VpnServiceException(message, innerException);
