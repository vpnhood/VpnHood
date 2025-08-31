namespace VpnHood.AppLib.Abstractions.AdExceptions;

public class AdException(string? message = null, Exception? innerException = null) 
    : Exception(message, innerException);
