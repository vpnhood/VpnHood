namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class UnreachableProxyServerException(string? message, Exception? innerException = null)
    : UnreachableServerException(message, innerException);