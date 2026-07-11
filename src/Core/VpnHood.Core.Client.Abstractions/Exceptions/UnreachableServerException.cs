namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class UnreachableServerException(string? message = null, Exception? innerException = null)
    : Exception(message, innerException);