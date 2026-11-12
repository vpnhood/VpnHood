namespace VpnHood.Core.Common.Exceptions;

/// <summary>
/// The exception that is thrown when another instance of the app is running.
/// </summary>
/// <param name="message">The message, or <see langword="null"/> for a standard one.</param>
/// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
public class AnotherInstanceIsRunningException(string? message = null, Exception? innerException = null)
    : Exception(message ?? "Another instance is running.", innerException);