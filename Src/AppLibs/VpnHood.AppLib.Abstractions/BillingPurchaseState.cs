using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Abstractions;

//todo: remove
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BillingPurchaseState
{
    None = 0,
    Started = 1,
    Processing = 2
}