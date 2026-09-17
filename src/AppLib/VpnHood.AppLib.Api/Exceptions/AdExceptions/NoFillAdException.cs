namespace VpnHood.AppLib.Api.Exceptions.AdExceptions;

public class NoFillAdException(string message, Exception? innerException = null)
    : LoadAdException(message, innerException);
