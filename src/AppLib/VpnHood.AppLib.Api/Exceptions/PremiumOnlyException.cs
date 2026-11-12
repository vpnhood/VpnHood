
using VpnHood.AppLib.Api.App;

namespace VpnHood.AppLib.Api.Exceptions;

/// <summary>Represents the error of a feature that is for premium only; its data's <c>Feature</c> names the feature.</summary>
public class PremiumOnlyException : UnauthorizedAccessException
{
    private PremiumOnlyException(string message) : base(message)
    {
    }

    /// <summary>Creates the error of a feature that is for premium only.</summary>
    /// <param name="feature">The feature.</param>
    /// <returns>The error.</returns>
    public static PremiumOnlyException Create(AppFeature feature)
    {
        // ReSharper disable once UseObjectOrCollectionInitializer
        var exception = new PremiumOnlyException($"Feature '{feature}' is available only for premium users.");
        exception.Data["Feature"] = feature;
        return exception;
    }
}