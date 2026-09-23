using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Portal.Dto;

/// <summary>
/// The whole answer of POST /billing/purchases. What the purchase delivered is deliberately not
/// repeated here: once provisioned, GET /account is where the code and the subscription live.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PortalPurchaseState>))]
public enum PortalPurchaseState
{
    /// <summary>Delivered — refresh GET /account for the code.</summary>
    Provisioned,

    /// <summary>The store has not settled the payment yet (deferred or slow payment methods).</summary>
    Pending
}
