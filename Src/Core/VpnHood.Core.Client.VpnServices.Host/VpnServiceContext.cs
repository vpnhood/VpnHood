using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Host;

internal class VpnServiceContext
{
    private readonly string _configFolder;

    public VpnServiceContext(string configFolder)
    {
        _configFolder = configFolder;
        ConnectionInfo = DefaultConnectionInfo;
    }

    public string ConfigFilePath => Path.Combine(_configFolder, ClientOptions.VpnConfigFileName);
    public string StatusFilePath => Path.Combine(_configFolder, ClientOptions.VpnStatusFileName);
    public string LogFilePath => Path.Combine(_configFolder, ClientOptions.VpnLogFileName);
    public string ConfigFolder => _configFolder;

    public static ConnectionInfo DefaultConnectionInfo { get; } = new ConnectionInfo {
        ApiEndPoint = null,
        ApiKey = null,
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

    public ConnectionInfo ConnectionInfo { get; private set; }

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

        // Use source-generated STJ (ClientOptionsJsonContext). Reflection-based STJ hangs/explodes
        // metadata inside the iOS NetworkExtension under Mono AOT.
        var opts = (ClientOptions?)JsonSerializer.Deserialize(json,
            typeof(ClientOptions), ClientOptionsJsonContext.Default);
        if (opts == null)
            throw new InvalidDataException("ClientOptions could not be deserialized!");
        return opts;
    }

    private readonly AsyncLock _connectionInfoLock = new();

    public async Task<bool> TryWriteConnectionInfo(ConnectionInfo connectionInfo, CancellationToken cancellationToken)
    {
        try {
            using var scopeLock = await _connectionInfoLock.LockAsync(cancellationToken);
            ConnectionInfo = connectionInfo;

            // source-generated STJ — reflection-based serialization hangs under iOS Mono AOT.
            var json = JsonSerializer.Serialize(connectionInfo, ApiTransportJsonContext.For<ConnectionInfo>());

            // iOS Mono interpreter: file I/O on the shared App Group container can hang the calling
            // thread for ~30s on first access. Push the write to the thread pool so the calling task
            // can complete, with a 2-second hard ceiling.
            var pathCopy = StatusFilePath;
            var writeTask = Task.Run(() => {
                try {
                    File.WriteAllText(pathCopy, json);
                    return true;
                }
                catch {
                    return false;
                }
            }, CancellationToken.None);

            var winner = await Task.WhenAny(writeTask, Task.Delay(TimeSpan.FromSeconds(2), cancellationToken))
                .ConfigureAwait(false);
            if (winner == writeTask)
                await writeTask.ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException) {
            return false; // operation was cancelled
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not save connection info to file. FilePath: {FilePath}",
                StatusFilePath);
            return false;
        }
    }
}
