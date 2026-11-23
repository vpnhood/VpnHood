using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Core.Client.VpnServices.Host.Sessions;

// Writes the state of the host's client for the app to read, at each change and for each answer. A stop's reason, a
// revoke's say, replaces the client's own error in every state built here, as this write and the stop's own may land in
// either order; it is read when the state is built, so one that comes while the write waits is not in it. Nothing is
// written for a client the host no longer follows, nor for an end that the service's own reconnect follows.
internal class ConnectionInfoUpdater(
    VpnServiceContext context,
    VpnServiceConnectState connectState,
    VpnServiceKillSwitch killSwitch)
{
    // the client's state as it is now
    public Task Update(VpnHoodClient client, CancellationToken cancellationToken)
    {
        return Update(client, isAnswer: false, cancellationToken);
    }

    // The state an answer to the app carries: the client's, where there is one, or else the session's end again, where
    // its write failed
    public async Task<ConnectionInfo> UpdateForAnswer(CancellationToken cancellationToken)
    {
        if (connectState.Client is { } client)
            await Update(client, isAnswer: true, cancellationToken).Vhc();
        else
            await context.TryRewriteEnd(context.SessionId, cancellationToken).Vhc();

        return context.ConnectionInfo;
    }

    // writes the client's end, with a stop's reason where there is one, whatever the proxies' save did
    public async Task WriteClientEnd(VpnHoodClient client, int sessionId)
    {
        await TryFlushProxyStatuses(client, CancellationToken.None).Vhc();
        await context.TryWriteConnectionInfo(sessionId,
            ConnectionInfoBuilder.Build(client, connectState.DisconnectException), CancellationToken.None).Vhc();
    }

    private async Task Update(VpnHoodClient client, bool isAnswer, CancellationToken cancellationToken)
    {
        await TryFlushProxyStatuses(client, cancellationToken).Vhc();

        // the session before the client check: one begun in between has detached this client already
        var sessionId = context.SessionId;
        if (client != connectState.Client)
            return;

        // judged by what would be written, as the end may be noticed meanwhile
        var connectionInfo = ConnectionInfoBuilder.Build(client, connectState.DisconnectException);
        if (connectState.IsReconnectEnd(connectionInfo))
            return;

        if (connectionInfo.ClientState is not (ClientState.Disconnecting or ClientState.Disposed)) {
            await context.TryWriteConnectionInfo(sessionId, connectionInfo, cancellationToken).Vhc();
            return;
        }

        // The session is on its way down. The app may lift the block as soon as it reads Disconnecting, so the
        // kill-switch changes end first; an answer does not wait for that, and returns the last state published. The
        // end itself is the host's to write, once the session has let go: here it is only rewritten if its write failed
        if (!isAnswer)
            await killSwitch.EndChanges(sessionId).Vhc();

        if (context.IsEnding(sessionId))
            await context.TryRewriteEnd(sessionId, cancellationToken).Vhc();
        else if (!isAnswer)
            await context.TryWriteConnectionInfo(sessionId,
                ConnectionInfoBuilder.Build(client, connectState.DisconnectException, ClientState.Disconnecting),
                cancellationToken).Vhc();
    }

    // Saves the proxy statuses first, so the app's process reads them fresh from the store it shares. A failure is
    // logged, not thrown, unless the caller canceled: the state goes to the app either way
    private static async Task TryFlushProxyStatuses(VpnHoodClient client, CancellationToken cancellationToken)
    {
        if (client.ProxyConnector is not { } proxyConnector)
            return;

        try {
            await proxyConnector.Flush(cancellationToken).Vhc();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) {
            VhLogger.Instance.LogError(ex, "Could not save the statuses of the proxies.");
        }
    }
}
