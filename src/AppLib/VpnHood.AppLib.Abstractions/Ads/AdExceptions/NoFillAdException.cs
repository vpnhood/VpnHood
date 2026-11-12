namespace VpnHood.AppLib.Abstractions.Ads.AdExceptions;

/// <summary>Represents the error of an ad the network had none to fill.</summary>
/// <param name="message">The error's message.</param>
/// <param name="innerException">The error that caused it, or <see langword="null"/>.</param>
public class NoFillAdException(string message, Exception? innerException = null)
    : LoadAdException(message, innerException);
