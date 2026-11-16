namespace VpnHood.Core.Client.Abstractions.Exceptions;

/// <summary>
/// The exception that is thrown when the VPN adapter stopped on its own (<c>IVpnAdapter.Failed</c>): its interface
/// disabled or gone, its I/O broken, or its restart failed.
/// </summary>
/// <param name="message">The message.</param>
/// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
/// <remarks>An adapter that fails to start fails the connection with its own error instead.</remarks>
public class VpnAdapterFailedException(string message, Exception? innerException = null)
    : Exception(message, innerException);
