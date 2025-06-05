using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Host;

internal class VpnServiceContext(string configFolder)
{
    public string ConfigFilePath => Path.Combine(configFolder, ClientOptions.VpnConfigFileName);
    public string StatusFilePath => Path.Combine(configFolder, ClientOptions.VpnStatusFileName);
    public string LogFilePath => Path.Combine(configFolder, ClientOptions.VpnLogFileName);
    public string ConfigFolder => configFolder;
    public ClientOptions ReadClientOptions()
    {
        // read from config file
        var json = File.ReadAllText(ConfigFilePath);
        return JsonUtils.Deserialize<ClientOptions>(json);
    }


    private readonly AsyncLock _connectionInfoLock = new();
    public async Task<bool> TryWriteConnectionInfo(ConnectionInfo connectionInfo, CancellationToken cancellationToken)
    {
        using var scopeLock = await _connectionInfoLock.LockAsync(cancellationToken);
        var json = JsonSerializer.Serialize(connectionInfo);

        try {
            await FileUtils.WriteAllTextRetryAsync(StatusFilePath, json, timeout: TimeSpan.FromSeconds(2), cancellationToken: cancellationToken);
            return true;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not save connection info to file. FilePath: {FilePath}", StatusFilePath);
            return false;
        }
    }

    public async Task<ConnectionInfo> ReadConnectionInfo()
    {
        using var scopeLock = await _connectionInfoLock.LockAsync();
        var json = await FileUtils.ReadAllTextAsync(StatusFilePath, TimeSpan.FromSeconds(1));
        return JsonUtils.Deserialize<ConnectionInfo>(json);
    }

    private static ConnectionInfo BuildDefaultConnectionInfo(
        byte[] apiKey, IPEndPoint? apiEndPoint, ClientState clientState = ClientState.None)
    {
        return new ConnectionInfo {
            ClientState = clientState,
            SessionInfo = null,
            SessionStatus = null,
            Error = null,
            ApiEndPoint = apiEndPoint,
            ApiKey = apiKey,
            HasSetByService = true
        };
    }

    public async Task<ConnectionInfo> ReadConnectionInfoOrDefault(byte[] apiKey, IPEndPoint? apiEndPoint)
    {
        return await TryReadConnectionInfo() ?? BuildDefaultConnectionInfo(apiKey, apiEndPoint);
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