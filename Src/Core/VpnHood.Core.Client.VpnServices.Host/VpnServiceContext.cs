using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Host;

internal class VpnServiceContext(string configFolder)
{
    public string ConfigFilePath => Path.Combine(configFolder, ClientOptions.VpnConfigFileName);
    public string StatusFilePath => Path.Combine(configFolder, ClientOptions.VpnStatusFileName);
    public string LogFilePath => Path.Combine(configFolder, ClientOptions.VpnLogFileName);
    public string ConfigFolder => configFolder;

    public static ConnectionInfo DefaultConnectionInfo { get; } = new() {
        ProxyManagerStatus = null,
        ClientState = ClientState.Initializing,
        ClientStateProgress = null,
        ClientStateChangedTime = null,
        CreatedTime = FastDateTime.UtcNow,
        Error = null,
        SessionInfo = null,
        SessionName = null,
        SessionStatus = null
    };

    public ConnectionInfo ConnectionInfo { get; private set; } = DefaultConnectionInfo;

    public ClientOptions? TryReadClientOptions()
    {
        try {
            return ReadClientOptions();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not read client options from file.");
            return null;
        }
    }

    public ClientOptions ReadClientOptions()
    {
        var json = File.ReadAllText(ConfigFilePath);

        var opts = JsonSerializer.Deserialize<ClientOptions>(json);
        if (opts == null)
            throw new InvalidDataException("ClientOptions could not be deserialized!");
        return opts;
    }

    private readonly AsyncLock _writeLock = new();

    public async Task<bool> TryWriteConnectionInfo(ConnectionInfo connectionInfo, CancellationToken cancellationToken)
    {
        try {
            using var scopeLock = await _writeLock.LockAsync(cancellationToken);
            ConnectionInfo = connectionInfo;

            var json = JsonSerializer.Serialize(connectionInfo);
            await File.WriteAllTextAsync(StatusFilePath, json, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) {
            return false;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not save connection info to file. FilePath: {FilePath}", StatusFilePath);
            return false;
        }
    }
}
