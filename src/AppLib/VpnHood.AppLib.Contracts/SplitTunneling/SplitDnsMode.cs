using System.Text.Json.Serialization;

namespace VpnHood.AppLib.Contracts.SplitTunneling;

// Where DNS traffic is allowed to go, independent of every other split. A resolver reachable AROUND
// the tunnel hands the local network the full list of names looked up and lets it answer them, which
// is why the default keeps all of it inside.
// Serialized by name: this is a saved setting as much as a wire value, so a member added here must
// never be able to reinterpret one already written to settings.json.
[JsonConverter(typeof(JsonStringEnumConverter<SplitDnsMode>))]
public enum SplitDnsMode
{
    IncludeAll,
    DefaultRoute
}
