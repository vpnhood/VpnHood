namespace VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;

public class VpnServiceUnreachableException(string message, Exception? innerException = null)
    : VpnServiceException(message, innerException);
