namespace VpnHood.Core.Common.Exceptions;

public class AnotherInstanceIsRunningException(string? message = null, Exception? innerException = null)
    : Exception(message ?? "Another instance is running.", innerException);