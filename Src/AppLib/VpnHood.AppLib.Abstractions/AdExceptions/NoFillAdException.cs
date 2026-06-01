namespace VpnHood.AppLib.Abstractions.AdExceptions;

public class NoFillAdException(string message, Exception? innerException = null)
    : LoadAdException(message, innerException);
