namespace VpnHood.Core.Client.Abstractions.Exceptions;

/// <summary>
/// The exception that is thrown when no server of the access key can be reached.
/// </summary>
/// <param name="message">The message, or <see langword="null"/> for a standard one.</param>
/// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
public class UnreachableServerException(string? message = null, Exception? innerException = null)
    : Exception(message ?? "There is no reachable server at this moment. Please try again later.", innerException);