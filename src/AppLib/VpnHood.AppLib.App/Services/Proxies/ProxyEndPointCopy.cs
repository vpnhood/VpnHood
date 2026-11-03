using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.AppLib.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Services.Proxies;

// The proxies the person added - by hand or as a pasted list; a URL's come back with the URL - kept in the
// app's file store as well, where the platform may delete the proxies' database
// (AppOptions.IsVpnServiceDataPurgeable).
public class ProxyEndPointCopy(IAppFileStore fileStore, string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public IReadOnlyList<ProxyEndPoint> Load()
    {
        var json = fileStore.Read(filePath);
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

    public void Save(IReadOnlyList<ProxyEndPoint> endPoints)
    {
        var items = endPoints
            .Select(x => new Item(x.Protocol, x.Host, x.Port, x.Username, x.Password, x.IsEnabled))
            .ToArray();
        fileStore.Write(filePath, JsonSerializer.Serialize(items, JsonOptions));
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
