namespace VpnHood.AppLib.Exceptions;

public class PremiumOnlyException : UnauthorizedAccessException
{
    private PremiumOnlyException(string message) : base(message)
    {
    }

    public static PremiumOnlyException Create(AppFeature feature)
    {
        // ReSharper disable once UseObjectOrCollectionInitializer
        var exception = new PremiumOnlyException($"Feature '{feature}' is available only for premium users.");
        exception.Data["Feature"] = feature;
        return exception;
    }
}
