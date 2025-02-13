using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.ToolKit;

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

    internal async Task SaveConnectionInfo(ConnectionInfo connectionInfo)
    {
        var json = JsonSerializer.Serialize(connectionInfo);

        try {
            await FileUtils.WriteAllTextAsync(StatusFilePath, json, timeout: TimeSpan.FromSeconds(1));
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not save connection info to file. FilePath: {FilePath}", StatusFilePath);
        }
    }
}