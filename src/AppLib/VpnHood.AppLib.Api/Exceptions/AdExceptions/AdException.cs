namespace VpnHood.AppLib.Api.Exceptions;

public class AdException(string? message = null, Exception? innerException = null)
    : Exception(message, innerException);