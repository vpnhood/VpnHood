using System.Text.Json;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.Core.Client.VpnServices.Manager;

// What the manager knows of the VPN service's state: the status file the service rewrites at each state change,
// and the last state read, written or answered
internal sealed class VpnServiceStatus
{
    private readonly string _filePath;

    public VpnServiceStatus(string configFolder)
    {
        _filePath = Path.Combine(configFolder, ClientOptions.VpnStatusFileName);
        ConnectionInfo = TryRead() ?? Build(ClientState.None);
    }

    public ConnectionInfo ConnectionInfo { get; set; }

    // The VPN service rewrites the status file at each state change. The read lets writers in: a reader that kept
    // them out would fail the service's write and lose that state, and a stop would wait out its timeout for a
    // Disposed that never came. A read that finds no file, or catches it half-written, is null: the next one gets it
    public ConnectionInfo? TryRead()
    {
        try {
            using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<ConnectionInfo>(stream);
        }
        catch {
            return null;
        }
    }

    public ConnectionInfo Set(ClientState clientState, Exception? ex = null)
    {
        ConnectionInfo = Build(clientState, ex: ex);
        try {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(ConnectionInfo));
        }
        catch {
            // it is ok if we can't write to the file
            // It means the service is overwriting the file
        }

        return ConnectionInfo;
    }

    private static ConnectionInfo Build(ClientState clientState, Exception? ex = null)
    {
        return new ConnectionInfo {
            ProxyConnectorStatus = null,
            CreatedTime = null,
            SessionInfo = null,
            SessionStatus = null,
            ClientState = clientState,
            ClientStateChangedTime = null,
            ClientStateProgress = null,
            Error = ex?.ToApiError()
        };
    }
}
