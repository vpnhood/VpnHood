namespace VpnHood.AppLib.Abstractions.Ads.AdExceptions;

/// <summary>Represents an error of an ad.</summary>
/// <param name="message">The error's message, or <see langword="null"/> for the default one.</param>
/// <param name="innerException">The error that caused it, or <see langword="null"/>.</param>
public class AdException(string? message = null, Exception? innerException = null)
    : Exception(message, innerException);