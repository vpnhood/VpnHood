namespace VpnHood.Core.Common.Exceptions;

public class GracefullyShutdownException(string? message = null, Exception? innerException = null)
    : Exception(message ?? "The application is shutting down gracefully.", innerException);