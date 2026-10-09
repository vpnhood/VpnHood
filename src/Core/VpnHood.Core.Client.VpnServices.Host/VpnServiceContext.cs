using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Host;

internal class VpnServiceContext(string configFolder)
{
    public VpnServiceOptionsFile ServiceOptionsFile { get; } = new(configFolder);
    public string StatusFilePath => Path.Combine(configFolder, ClientOptions.VpnStatusFileName);
    public string LogFilePath => Path.Combine(configFolder, ClientOptions.VpnLogFileName);
    public string ConfigFolder => configFolder;

    public ConnectionInfo ConnectionInfo { get; private set; } = ConnectionInfo.Default;

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
