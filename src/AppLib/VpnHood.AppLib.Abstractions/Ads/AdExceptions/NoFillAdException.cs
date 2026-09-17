namespace VpnHood.AppLib.Abstractions.Ads.AdExceptions;

public class NoFillAdException(string message, Exception? innerException = null)
    : LoadAdException(message, innerException);
