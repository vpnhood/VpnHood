namespace VpnHood.AppLib.Abstractions.AdExceptions;

public class RewardNotEarnedException(string message, Exception? innerException = null)
    : AdException(message, innerException);
