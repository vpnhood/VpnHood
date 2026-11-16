namespace VpnHood.Core.Common.Exceptions;

/// <summary>
/// The exception that is thrown when no TCP endpoint of the server works after its configuration.
/// </summary>
/// <param name="message">The message, or <see langword="null"/> for a standard one.</param>
/// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
public class NoWorkingEndPointException(string? message = null, Exception? innerException = null)
    : Exception(message ?? "No working tcp end point is available after configuration!", innerException);
