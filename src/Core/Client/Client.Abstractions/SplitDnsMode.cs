using System.Text.Json.Serialization;

namespace VpnHood.Core.Client.Abstractions;

// Where DNS traffic is allowed to go, independent of every other split. A resolver reachable AROUND the
// tunnel is a leak of its own kind: it hands the local network the full list of names the user looks up and
// lets it answer them, so any split that pushes DNS out weakens the tunnel far beyond the addresses it meant
// to exclude. That is why the default keeps all of it inside.
// Serialized by name (settings.json and vpn.config): the stored value stays readable and adding a mode can
// never reinterpret a saved one.
[JsonConverter(typeof(JsonStringEnumConverter<SplitDnsMode>))]
public enum SplitDnsMode
{
    // Every DNS query goes through the tunnel, past any split that would push it out. A query the server
    // can not deliver dies at the server rather than being sent around the tunnel: a dead query is a
    // visible failure, a leaked one is silent. Include or drop, never leak. Whether the local network is
    // captured at all remains the device include set's business. This is the default.
    IncludeAll,

    // No DNS rule at all: DNS follows the same splits as any other traffic. Leaks whatever they exclude —
    // kept as the escape hatch for a setup the forcing breaks.
    DefaultRoute
}
