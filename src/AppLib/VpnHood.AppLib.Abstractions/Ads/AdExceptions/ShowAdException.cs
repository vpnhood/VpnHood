namespace VpnHood.AppLib.Abstractions.Ads.AdExceptions;

/// <summary>Represents the error of an ad that could not be shown.</summary>
/// <param name="message">The error's message.</param>
/// <param name="innerException">The error that caused it, or <see langword="null"/>.</param>
public class ShowAdException(string message, Exception? innerException = null)
    : AdException(message, innerException)
{
    /// <summary>
    /// Gets or sets the name of the app's ad provider entry that failed, as the app's ad options name it, or
    /// <see langword="null"/>.
    /// </summary>
    public string? AdNetworkName { get; set; }
}
