namespace VpnHood.AppLib.Abstractions.Ads.AdExceptions;

/// <summary>Represents the error of an ad that could not be loaded.</summary>
/// <param name="message">The error's message, or <see langword="null"/> for the default one.</param>
/// <param name="innerException">The error that caused it, or <see langword="null"/>.</param>
public class LoadAdException(string? message = null, Exception? innerException = null)
    : AdException(message, innerException);