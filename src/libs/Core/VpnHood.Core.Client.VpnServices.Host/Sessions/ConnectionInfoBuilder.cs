using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Host.Sessions;

// Builds the connection's info, the state the app reads
internal static class ConnectionInfoBuilder
{
    // exception: a stop's reason, which replaces the client's own error. clientState: the state to report, or null for
    // the client's own. A client that has not started is reported as Initializing: None would tell the app that no
    // service runs, and a stop would take it for the end
    public static ConnectionInfo Build(VpnHoodClient client, Exception? exception, ClientState? clientState = null)
    {
        var state = clientState ?? client.State;
        return new ConnectionInfo {
            CreatedTime = FastDateTime.UtcNow,
            ProxyConnectorStatus = TryGetProxyConnectorStatus(client),
            SessionName = client.Config.SessionName,
            SessionInfo = client.Session?.Info,
            SessionStatus = client.Session?.Status.ToDto(),
            ClientState = state is ClientState.None ? ClientState.Initializing : state,
            ClientStateProgress = client.StateProgress,
            ClientStateChangedTime = client.StateChangedTime,
            Error = exception?.ToApiError() ?? client.LastException?.ToApiError(),
            VpnEngineInfo = VpnEngineInfo.VpnHoodEngine
        };
    }

    // the host's own word, with no client to tell it
    public static ConnectionInfo BuildWithoutClient(ClientState clientState, string? sessionName, Exception? exception)
    {
        return new ConnectionInfo {
            CreatedTime = FastDateTime.UtcNow,
            ProxyConnectorStatus = null,
            SessionInfo = null,
            SessionStatus = null,
            ClientState = clientState,
            ClientStateProgress = null,
            ClientStateChangedTime = null,
            Error = exception?.ToApiError(),
            SessionName = sessionName,
            VpnEngineInfo = VpnEngineInfo.VpnHoodEngine
        };
    }

    // The proxies' state, or null where the connector refuses, as one of another's making may once closed: the state
    // goes to the app whatever the proxies' part does
    private static ProxyConnectorStatus? TryGetProxyConnectorStatus(VpnHoodClient client)
    {
        try {
            return client.ProxyConnector?.Status;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not read the status of the proxies.");
            return null;
        }
    }
}
