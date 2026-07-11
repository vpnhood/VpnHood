namespace VpnHood.Core.Tunneling.Exceptions;

public class NatEndPointNotFoundException(string? message = null, Exception? innerException = null)
    : Exception(message ?? "The specified endpoint could not be found in NAT.", innerException);
