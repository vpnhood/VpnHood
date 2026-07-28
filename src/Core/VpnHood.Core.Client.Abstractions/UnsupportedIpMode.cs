using System.Text.Json.Serialization;

namespace VpnHood.Core.Client.Abstractions;

// What happens to a destination the server does not route (outside its declared include ranges) when no
// split of the user's own has already excluded it. The user's splits are never subject to this: a client
// Exclude or Block always wins — this mode only decides the fate of traffic that WANTED the tunnel and was
// refused by the server's word.
// Serialized by name (settings.json and vpn.config): the stored value stays readable and adding a mode can
// never reinterpret a saved one.
[JsonConverter(typeof(JsonStringEnumConverter<UnsupportedIpMode>))]
public enum UnsupportedIpMode
{
    // Unsupported destinations connect directly, outside the VPN. Keeps everything working (the classic
    // behavior) at the cost of a leak: an observer sees the traffic the server declined. This is the default.
    Exclude,

    // Unsupported destinations are dropped, never sent around the tunnel — fail-closed. Also requires the
    // adapter to capture them (the capture set ignores the server's adapter ranges in this mode): an
    // uncaptured packet is routed by the OS and can not be blocked.
    Block
}
