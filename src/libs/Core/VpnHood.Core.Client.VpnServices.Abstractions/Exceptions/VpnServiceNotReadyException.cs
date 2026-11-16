namespace VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;

/// <summary>
/// The exception that is thrown when the VPN service cannot take a request yet: it has not published its endpoint, or
/// its last state is an error.
/// </summary>
/// <param name="message">The message.</param>
/// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
public class VpnServiceNotReadyException(string message, Exception? innerException = null)
    : VpnServiceException(message, innerException);
