namespace VpnHood.Core.Tunneling.Exceptions;

/// <summary>
/// Represents the error a session's request meets where the server's filter blocks its destination.
/// </summary>
/// <param name="message">The error's message, or <see langword="null"/> for the default one.</param>
/// <param name="innerException">The error that caused it, or <see langword="null"/>.</param>
public class NetFilterException(string? message = null, Exception? innerException = null)
    : Exception(message, innerException);