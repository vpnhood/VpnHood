namespace VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;

public class VpnServiceException(string message, Exception? innerException = null)
    : Exception(message, innerException);
