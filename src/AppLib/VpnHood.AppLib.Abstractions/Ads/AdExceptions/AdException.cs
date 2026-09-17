namespace VpnHood.AppLib.Abstractions.Ads.AdExceptions;

public class AdException(string? message = null, Exception? innerException = null)
    : Exception(message, innerException);