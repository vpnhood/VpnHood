namespace VpnHood.AppLib.Abstractions;

public class AppPurchaseAttribution
{
    /// <summary>GooglePlay obfuscated account id that ties the store order to the backend user.</summary>
    public string? AccountId { get; init; }

    /// <summary>AppStore appAccountToken. Apple requires a UUID.</summary>
    public Guid? AppAccountToken { get; init; }

    /// <summary>MicrosoftStore service ticket used to acquire the customer store id key.</summary>
    public string? StoreServiceTicket { get; init; }
}
