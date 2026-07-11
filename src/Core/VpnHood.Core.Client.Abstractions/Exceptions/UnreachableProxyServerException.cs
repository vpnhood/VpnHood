namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class UnreachableProxyServerException(string? message = null, Exception? innerException = null)
    : UnreachableServerException(message, innerException);