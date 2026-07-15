using System.Text.Json.Serialization;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Proxies.EndPointManagement.Abstractions.Options;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

/// <summary>
/// What the app hands to the VpnService: the vpn.config root. It holds the storage-backed configuration the
/// service must resolve into services itself — filter dbs, proxy endpoints — and nests the smaller
/// <see cref="ClientOptions"/> that the engine needs. VpnHoodClient only ever receives that nested object,
/// so it stays blind to paths and stores.
/// </summary>
public class VpnServiceOptions
{
    public required ClientOptions ClientOptions { get; set; }

    // Split-ip db paths: one self-describing SQLite db per context (split-country, split-ip-via-app). Each
    // db carries its own include/exclude/block sets, so no action travels with it, and the ranges themselves
    // never travel cross-process — the service chains one read-only SqliteIpFilter per path.
    // See docs/split-ip/README.md.
    public string[] SplitIpDbPaths { get; set; } = [];

    // Split-domain db paths: the string twin of SplitIpDbPaths (split-domain). Each db carries its own
    // include/exclude/block domain sets; the service chains one read-only SqliteDomainFilter per path.
    // Unlike the ip sets, the include set is the override lane: a member domain is forced through the
    // tunnel past any ip-gate veto.
    public string[] SplitDomainDbPaths { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ProxyOptions? ProxyOptions { get; set; }
}
