namespace VpnHood.AppLib.Ios.AppStore.StoreKitBridge;

/// <summary>A sellable product as the Swift facade reports it from StoreKit 2.</summary>
public class StoreKitProduct
{
    public required string Id { get; init; }

    /// <summary>Decimal price in the store currency (the recurring, non-introductory price).</summary>
    public required double Price { get; init; }

    /// <summary>First price actually paid: the introductory offer if one applies, else Price.</summary>
    public required double CurrentPrice { get; init; }

    /// <summary>ISO 8601 duration of the billing period, e.g. "P1M".</summary>
    public required string PeriodIso { get; init; }

    /// <summary>ISO 8601 duration of the free-trial phase, when the eligible offer has one.</summary>
    public string? TrialPeriodIso { get; init; }

    public required string CurrencyCode { get; init; }
    public required string CurrencySymbol { get; init; }
}
