using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Contracts.Billing;

[JsonConverter(typeof(JsonStringEnumConverter<PurchaseState>))]
public enum PurchaseState
{
    None = 0,
    Started = 1,
    Processing = 2
}
