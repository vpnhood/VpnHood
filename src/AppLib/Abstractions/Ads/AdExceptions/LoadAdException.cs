namespace VpnHood.AppLib.Abstractions.Ads.AdExceptions;

public class LoadAdException(string? message = null, Exception? innerException = null)
    : AdException(message, innerException);