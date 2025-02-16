using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.ToolKit;

namespace VpnHood.Core.Client.VpnServicing;

internal class VpnHoodServiceContext(string configFolder)
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

    public async Task WriteConnectionInfo(ConnectionInfo connectionInfo)
    {
        var json = JsonSerializer.Serialize(connectionInfo);

        try {
            await FileUtils.WriteAllTextRetryAsync(StatusFilePath, json, timeout: TimeSpan.FromSeconds(1));
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not save connection info to file. FilePath: {FilePath}", StatusFilePath);
        }
    }

    public async Task<ConnectionInfo> ReadConnectionInfo()
    {
        var json = await FileUtils.ReadAllTextAsync(StatusFilePath, TimeSpan.FromSeconds(1));
        return JsonUtils.Deserialize<ConnectionInfo>(json);
    }

    public async Task<ConnectionInfo> ReadConnectionInfoOrDefault(byte[] apiKey, IPEndPoint apiEndPoint)
    {
        return await TryReadConnectionInfo() ??
               new ConnectionInfo {
                   ClientState = ClientState.None,
                   SessionInfo = null,
                   SessionStatus = null,
                   Error = null,
                   ApiEndPoint = apiEndPoint,
                   ApiKey = apiKey
               };
    }

    public async Task<ConnectionInfo?> TryReadConnectionInfo()
    {
        try {
            if (!File.Exists(StatusFilePath))
                return null;

            return await ReadConnectionInfo();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not read connection info from file. FilePath: {FilePath}", StatusFilePath);
            return null;
        }
    }
}