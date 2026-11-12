using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Net.Toolkit.Storages;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Services.Proxies;

/// <summary>
/// Provides a copy of the proxies the person added, by hand or as a pasted list, in the app's storage, where the
/// platform may delete the proxies' database, as tvOS does when the device runs low on storage.
/// </summary>
/// <param name="storage">The storage the copy is kept in.</param>
public class ProxyEndPointCopy(Storage storage)
{
    private const string FileName = "proxies.json";

    private static readonly JsonSerializerOptions JsonOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    internal IReadOnlyList<ProxyEndPoint> Load()
    {
        var json = storage.Read(FileName);
        return json is null
            ? []
            : JsonUtils.Deserialize<Item[]>(json, JsonOptions).Select(item => new ProxyEndPoint {
                Protocol = item.Protocol,
                Host = item.Host,
                Port = item.Port,
                Username = item.Username,
                Password = item.Password,
                IsEnabled = item.IsEnabled
            }).ToArray();
    }

    internal void Save(IReadOnlyList<ProxyEndPoint> endPoints)
    {
        var items = endPoints
            .Select(x => new Item(x.Protocol, x.Host, x.Port, x.Username, x.Password, x.IsEnabled))
            .ToArray();
        storage.Write(FileName, JsonSerializer.Serialize(items, JsonOptions));
    }

    // the endpoint's own fields, as it has them: its Url leaves a disabled one's flag out
    private sealed record Item(
        ProxyProtocol Protocol,
        string Host,
        int Port,
        string? Username,
        string? Password,
        bool IsEnabled);
}
