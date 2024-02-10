using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.Persistence.Utils;

public class ServerStateResolver
{
    public required ServerStatusBaseModel? ServerStatus { get; init; }
    public required TimeSpan LostServerThreshold { get; init; }
    public required DateTime? ConfigureTime { get; init; }
    public required Guid ConfigCode { get; init; }
    public required Guid? LastConfigCode { get; init; }
    public required bool IsEnabled { get; set; }

    public ServerState State
    {
        get
        {
            if (ConfigureTime == null) return ServerState.NotInstalled;
            if (ServerStatus == null || ServerStatus.CreatedTime < DateTime.UtcNow - LostServerThreshold)
                return ServerState.Lost;
            if (ConfigCode != LastConfigCode) return ServerState.Configuring;
            if (!IsEnabled) return ServerState.Disabled;
            if (ServerStatus.SessionCount == 0) return ServerState.Idle;
            return ServerState.Active;
        }
    }

    public bool IsReady
    {
        get
        {
            return State is ServerState.Idle or ServerState.Active;
        }
    }
}