namespace VpnHood.AppLib.Api.Exceptions.AdExceptions;

public class RewardNotEarnedException(string message, Exception? innerException = null)
    : AdException(message, innerException);
