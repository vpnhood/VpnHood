using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Abstractions.Billing;

/// <summary>Specifies where the store's payment flow stands.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PurchaseState>))]
public enum PurchaseState
{
    /// <summary>No payment is under way.</summary>
    None = 0,

    /// <summary>A purchase or restoration has started.</summary>
    Started = 1,

    /// <summary>The store has given its proof of purchase, and the purchase is being redeemed.</summary>
    Processing = 2
}
