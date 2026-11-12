namespace VpnHood.AppLib.Abstractions.Ads.AdExceptions;

/// <summary>
/// Represents the error of an ad that needs the app's window in front and found none, before the ad or right after it.
/// </summary>
/// <param name="message">The error's message, or <see langword="null"/> for one that says so.</param>
/// <param name="innerException">The error that caused it, or <see langword="null"/>.</param>
public class ShowAdNoUiException(string? message = null, Exception? innerException = null)
    : ShowAdException(message ?? DefaultMessage, innerException)
{
    private const string DefaultMessage = "Could not show any ad because the app window was not open.";
}
