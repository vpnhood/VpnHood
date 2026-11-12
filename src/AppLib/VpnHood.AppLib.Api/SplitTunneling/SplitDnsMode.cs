using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Api.SplitTunneling;

// Where DNS traffic is allowed to go, independent of every other split. A resolver reachable AROUND
// the tunnel hands the local network the full list of names looked up and lets it answer them, which
// is why the default keeps all of it inside.
// Serialized by name: this is a saved setting as much as a wire value, so a member added here must
// never be able to reinterpret one already written to settings.json.

/// <summary>
/// Specifies where DNS may go, whatever the other splits say.
/// </summary>
/// <remarks>
/// A resolver reached around the tunnel shows the local network every name the user looks up, and lets it answer, so
/// the default keeps DNS inside. In JSON it is a name; a name this version does not know fails to read.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<SplitDnsMode>))]
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
