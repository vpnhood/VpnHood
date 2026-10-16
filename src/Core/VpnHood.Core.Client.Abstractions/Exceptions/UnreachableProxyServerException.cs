namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class UnreachableProxyServerException(string? message = null, Exception? innerException = null)
    : UnreachableServerException(message ?? "All proxies are currently unreachable.", innerException);