using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
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
    private int _sessionId; // the current session's: an older one's words go unwritten
    private int _endedSessionId; // the last session whose first Disposed, its last word, is written

    public int SessionId => _sessionId;
    public int BeginSession() => Interlocked.Increment(ref _sessionId);

    // A session's words while it is the current one, up to its first Disposed: the app's stop takes that for the end
    // before it starts the next session
    public async Task<bool> TryWriteConnectionInfo(int sessionId, ConnectionInfo connectionInfo,
        CancellationToken cancellationToken)
    {
        try {
            using var scopeLock = await _writeLock.LockAsync(cancellationToken).Vhc();
            if (sessionId != _sessionId || sessionId == _endedSessionId)
                return false;

            if (connectionInfo.ClientState is ClientState.Disposed)
                _endedSessionId = sessionId;

            ConnectionInfo = connectionInfo;

            var json = JsonSerializer.Serialize(connectionInfo);
            await File.WriteAllTextAsync(StatusFilePath, json, cancellationToken).Vhc();
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
