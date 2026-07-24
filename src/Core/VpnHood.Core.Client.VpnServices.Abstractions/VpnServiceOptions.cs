using System.Text.Json.Serialization;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;

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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ProxyOptions? ProxyOptions { get; set; }
}
