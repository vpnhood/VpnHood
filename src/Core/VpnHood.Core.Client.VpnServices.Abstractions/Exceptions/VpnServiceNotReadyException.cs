namespace VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;

public class VpnServiceNotReadyException(string message, Exception? innerException = null)
    : VpnServiceException(message, innerException);
