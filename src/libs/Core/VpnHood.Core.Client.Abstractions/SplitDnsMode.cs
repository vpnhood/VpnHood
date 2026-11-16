using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Core.Client.Abstractions;

// Where DNS traffic is allowed to go, independent of every other split. A resolver reachable AROUND the
// tunnel is a leak of its own kind: it hands the local network the full list of names the user looks up and
// lets it answer them, so any split that pushes DNS out weakens the tunnel far beyond the addresses it meant
// to exclude. That is why the default keeps all of it inside.
// Serialized by name (settings.json and vpn.config): the stored value stays readable and adding a mode can
// never reinterpret a saved one.
/// <summary>
/// Specifies where DNS may go, whatever the other splits say.
/// </summary>
/// <remarks>
/// A resolver reached around the tunnel shows the local network every name the user looks up, and lets it answer, so
/// the default keeps DNS inside. In JSON it is a name; a name this version does not know reads as
/// <see cref="IncludeAll"/>.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumFallbackConverter<SplitDnsMode>))]
[JsonEnumFallback(SplitDnsMode.IncludeAll)]
public enum SplitDnsMode
{
    /// <summary>
    /// DNS the tunnel captures goes through it, past any split that would send it out, though a filter's block still
    /// drops it; a query the server cannot deliver fails there rather than going around the tunnel. The default.
    /// </summary>
    /// <remarks>
    /// DNS is told by its destination port: 53, or 853 for DNS over TLS or QUIC. DNS over HTTPS, on port 443, cannot be
    /// told from other HTTPS and follows the splits. What the device's ranges leave uncaptured, as the local network may
    /// be, never reaches the tunnel.
    /// </remarks>
    IncludeAll,

    /// <summary>
    /// No rule for DNS: it follows the splits as any traffic does, and leaks what they exclude. A way out for a setup the
    /// default breaks.
    /// </summary>
    DefaultRoute
}
