namespace VpnHood.AppLib.Abstractions.Ads.AdExceptions;

/// <summary>Represents the error of a rewarded ad whose reward the network did not confirm in time after it closed.</summary>
/// <param name="message">The error's message.</param>
/// <param name="innerException">The error that caused it, or <see langword="null"/>.</param>
public class RewardNotEarnedException(string message, Exception? innerException = null)
    : AdException(message, innerException);
