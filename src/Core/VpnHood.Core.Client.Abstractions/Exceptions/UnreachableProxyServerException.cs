namespace VpnHood.Core.Client.Abstractions.Exceptions;

/// <summary>
/// The exception that is thrown when no proxy the client connects through can be reached.
/// </summary>
/// <param name="message">The message, or <see langword="null"/> for a standard one.</param>
/// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
public class UnreachableProxyServerException(string? message = null, Exception? innerException = null)
    : UnreachableServerException(message ?? "All proxies are currently unreachable.", innerException);