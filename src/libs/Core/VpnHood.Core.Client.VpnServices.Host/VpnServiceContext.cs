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
    private readonly AsyncLock _writeLock = new();
    private SessionWord _word = new() { SessionId = 0, ConnectionInfo = ConnectionInfo.Default }; // the last, its session's
    private int _sessionId; // the current session's: an older one's words are refused
    private int _endingSessionId; // the last session whose Disposed has come: only another Disposed may follow it
    private int _endedSessionId; // the last session whose first Disposed, its last word, is written

    // The lock of the host's connects and of its tunnel hold (VpnServiceConnectState, VpnServiceTunnel). A connect
    // begins its session under it, so a check of the session and a change made under it are one step. The status file,
    // the notification and the kill switch have their own locks
    public Lock SessionLock { get; } = new();

    public VpnServiceOptionsFile ServiceOptionsFile { get; } = new(configFolder);
    public string StatusFilePath => Path.Combine(configFolder, ClientOptions.VpnStatusFileName);
    public string LogFilePath => Path.Combine(configFolder, ClientOptions.VpnLogFileName);
    public string ConfigFolder => configFolder;

    // The current session's last word, as the app is answered with it: Initializing where the session has given none
    // yet, as one just begun, before its first write, whose last session's word, its end say, the app would take for
    // this one's
    public ConnectionInfo ConnectionInfo {
        get {
            var word = Volatile.Read(ref _word);
            return word.SessionId == SessionId ? word.ConnectionInfo : CreateInitializing();
        }
    }

    // read before what the session's begin changed: the host detaches a client before its next session begins, so a
    // look at the session and then the client never finds an old client in a new session
    public int SessionId => Volatile.Read(ref _sessionId);
    public int BeginSession() => Interlocked.Increment(ref _sessionId);

    // whether the session's end has come: its first Disposed is given, written or not
    public bool IsEnding(int sessionId) => Volatile.Read(ref _endingSessionId) == sessionId;

    // A session's words while it is the current one, up to its first Disposed written: the app's stop takes that for
    // the end before it starts the next session. An end whose write failed stands, in the state the app is answered
    // with too: a word the session sent before its end would undo it, so after it only the end is written, again, the
    // first end as it was given, with its reason, whichever later end of the session asks
    public async Task<bool> TryWriteConnectionInfo(int sessionId, ConnectionInfo connectionInfo,
        CancellationToken cancellationToken)
    {
        try {
            using var scopeLock = await _writeLock.LockAsync(cancellationToken).Vhc();
            if (sessionId != _sessionId || sessionId == _endedSessionId)
                return false;

            var isEnd = connectionInfo.ClientState is ClientState.Disposed;
            if (sessionId == _endingSessionId && !isEnd)
                return false;

            var word = sessionId == _endingSessionId ? _word.ConnectionInfo : connectionInfo;
            if (isEnd)
                Volatile.Write(ref _endingSessionId, sessionId);

            Volatile.Write(ref _word, new SessionWord { SessionId = sessionId, ConnectionInfo = word });
            await WriteFile(word, cancellationToken).Vhc();
            if (isEnd)
                _endedSessionId = sessionId;

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

    // The session's end again, as it was given, its reason too, where its write failed: nothing where the session is not
    // the current one, or its end has not come, or is written
    public async Task<bool> TryRewriteEnd(int sessionId, CancellationToken cancellationToken)
    {
        try {
            using var scopeLock = await _writeLock.LockAsync(cancellationToken).Vhc();
            if (sessionId != _sessionId || sessionId != _endingSessionId || sessionId == _endedSessionId)
                return false;

            await WriteFile(_word.ConnectionInfo, cancellationToken).Vhc();
            _endedSessionId = sessionId;
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

    // under the write lock
    private Task WriteFile(ConnectionInfo connectionInfo, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(connectionInfo);
        return File.WriteAllTextAsync(StatusFilePath, json, cancellationToken);
    }

    // the word of a session that has given none yet
    private static ConnectionInfo CreateInitializing()
    {
        return new ConnectionInfo {
            CreatedTime = FastDateTime.UtcNow,
            ProxyConnectorStatus = null,
            ClientState = ClientState.Initializing,
            ClientStateProgress = null,
            ClientStateChangedTime = null,
            Error = null,
            SessionInfo = null,
            SessionName = null,
            SessionStatus = null,
            VpnEngineInfo = VpnEngineInfo.VpnHoodEngine
        };
    }

    // a session's word, as it was given
    private sealed class SessionWord
    {
        public required int SessionId { get; init; }
        public required ConnectionInfo ConnectionInfo { get; init; }
    }
}
