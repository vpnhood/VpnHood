using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Client.Abstractions;

// What happens to a destination the server does not route (outside its declared include ranges) when no
// split of the user's own has already excluded it. The user's splits are never subject to this: a client
// Exclude or Block always wins — this mode only decides the fate of traffic that WANTED the tunnel and was
// refused by the server's word.
// Serialized by name (settings.json and vpn.config): the stored value stays readable and adding a mode can
// never reinterpret a saved one.
/// <summary>
/// Specifies what happens to traffic that wants the tunnel and is for a destination the server does not route: outside
/// its ranges, or of an IP version it does not carry.
/// </summary>
/// <remarks>
/// The user's own exclusions and blocks always win. In JSON it is a name; a name this version does not know reads as
/// <see cref="Block"/>, which never sends traffic around the tunnel.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumFallbackConverter<SplitUnsupportedIpMode>))]
[JsonEnumFallback(SplitUnsupportedIpMode.Block)]
public enum SplitUnsupportedIpMode
{
    /// <summary>
    /// The traffic goes directly, outside the VPN: everything keeps working, and an observer sees the traffic the
    /// server declined.
    /// </summary>
    Exclude,

    /// <summary>
    /// The traffic is dropped, never sent around the tunnel. The adapter then captures it, whatever the server's adapter
    /// ranges say, as a packet it does not capture cannot be blocked.
    /// </summary>
    Block
}
