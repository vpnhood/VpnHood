namespace VpnHood.AppLib.Abstractions.AdExceptions;

public class LoadAdException(string? message = null, Exception? innerException = null)
    : AdException(message, innerException);