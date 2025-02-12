using System.Text.Json;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Utils;

namespace VpnHood.Core.Client;

public class VpnHoodServiceContext(string configFolder)
{
    public string ConfigFilePath => Path.Combine(configFolder, ClientOptions.VpnConfigFileName);
    public string StatusFilePath => Path.Combine(configFolder, ClientOptions.VpnStatusFileName);
    public string ConfigFolder => configFolder;
    public ClientOptions ReadClientOptions()
    {
        // read from config file
        var json = File.ReadAllText(ConfigFilePath);
        return JsonUtils.Deserialize<ClientOptions>(json);
    }

    internal void SaveConnectionInfo(ConnectionInfo connectionInfo)
    {
        var json = JsonSerializer.Serialize(connectionInfo);
        File.WriteAllText(StatusFilePath, json);
    }
}