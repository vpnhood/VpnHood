namespace VpnHood.AppLib.Api.Exceptions;

public class LoadAdException(string? message = null, Exception? innerException = null)
    : AdException(message, innerException);