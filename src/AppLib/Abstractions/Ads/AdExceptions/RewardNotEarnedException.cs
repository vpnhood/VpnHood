namespace VpnHood.AppLib.Abstractions.Ads.AdExceptions;

public class RewardNotEarnedException(string message, Exception? innerException = null)
    : AdException(message, innerException);
