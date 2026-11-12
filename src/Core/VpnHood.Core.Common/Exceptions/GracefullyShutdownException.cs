namespace VpnHood.Core.Common.Exceptions;

/// <summary>
/// The exception that is thrown to end an operation because the app is shutting down gracefully.
/// </summary>
/// <param name="message">The message, or <see langword="null"/> for a standard one.</param>
/// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
public class GracefullyShutdownException(string? message = null, Exception? innerException = null)
    : Exception(message ?? "The application is shutting down gracefully.", innerException);