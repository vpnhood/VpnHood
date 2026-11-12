namespace VpnHood.Core.Client.Abstractions.Exceptions;

/// <summary>
/// The exception that is thrown when the user cancels an operation, as by refusing the VPN's permission.
/// </summary>
/// <param name="message">The message.</param>
/// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
public class UserCanceledException(string message, Exception? innerException = null)
    : OperationCanceledException(message, innerException);
