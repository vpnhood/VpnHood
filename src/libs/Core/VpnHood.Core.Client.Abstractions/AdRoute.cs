using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Client.Abstractions;

/// <summary>
/// Specifies how an ad's traffic goes while the session waits for it. A connect's ad is tried on each route in this
/// order, until one shows; an ad during a session starts at <see cref="Passthrough"/>.
/// </summary>
/// <remarks>
/// In JSON it is a name; a name this version does not know reads as <see cref="OverVpn"/>, which only the ad networks
/// that can show over the VPN fill, so a newer route never loads one that refuses VPN traffic.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumFallbackConverter<AdRoute>))]
[JsonEnumFallback(OverVpn)]
public enum AdRoute
{
    /// <summary>Before the VPN adapter starts: the ad loads over the device's own network, and its network sees no VPN.</summary>
    BeforeVpn,

    /// <summary>
    /// With the VPN up, the session sends its new traffic around the tunnel, but the DNS and pings it carries as packets;
    /// once the try ends, the session drops its connections, so they come back through the tunnel.
    /// </summary>
    Passthrough,

    /// <summary>
    /// Through the tunnel, as the session's other traffic goes. Tried only where the app allows it for the kind of ad
    /// (<see cref="ClientOptions.CanShowFlexibleAdOverVpn"/>, <see cref="ClientOptions.CanShowRewardedAdOverVpn"/>).
    /// </summary>
    OverVpn
}
