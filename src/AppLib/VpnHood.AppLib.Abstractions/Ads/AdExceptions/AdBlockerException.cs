
namespace VpnHood.AppLib.Abstractions.Ads.AdExceptions;

/// <summary>
/// Represents the error of an ad that failed while the device's settings point to an ad blocker, as a private DNS the
/// app does not allow.
/// </summary>
/// <param name="message">The error's message.</param>
public class AdBlockerException(string message) : AdException(message)
{
    /// <summary>Gets or sets a value indicating whether the setting is the device's private DNS.</summary>
    public bool IsPrivateDns {
        get => Data["IsPrivateDns"] is true;
        set => Data["IsPrivateDns"] = value;
    }
}