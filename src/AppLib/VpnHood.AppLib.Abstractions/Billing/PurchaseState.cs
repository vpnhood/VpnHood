using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Abstractions.Billing;

[JsonConverter(typeof(JsonStringEnumConverter<PurchaseState>))]
public enum PurchaseState
{
    None = 0,
    Started = 1,
    Processing = 2
}
