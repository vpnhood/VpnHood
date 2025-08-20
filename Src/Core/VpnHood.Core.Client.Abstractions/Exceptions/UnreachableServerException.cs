namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class UnreachableServerException(string? message, Exception? innerException = null)
    : Exception(message, innerException);