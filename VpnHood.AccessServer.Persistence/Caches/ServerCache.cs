using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.Common;

namespace VpnHood.AccessServer.Persistence.Caches;

public class ServerCache
{
    public required Guid ProjectId { get; init; }
    public required Guid ServerId { get; init; }
    public required Guid ServerFarmId { get; init; }
    public required Guid ServerProfileId { get; set; }
    public required string? ServerFarmName { get; init; }
    public required string? ServerName { get; init; }
    public required string? Version { get; init; }
    public required string? LastConfigError { get; init; }
    public required Guid? LastConfigCode { get; init; }
    public required Guid ConfigCode { get; init; }
    public required DateTime? ConfigureTime { get; init; }
    public required bool IsEnabled { get; init; }
    public required Guid AuthorizationCode { get; init; }
    public required ServerLocationInfo LocationInfo { get; init; }
    public required int LogicalCoreCount { get; init; }
    public required AccessPointModel[] AccessPoints { get; init; }
    public required ServerStatusBaseModel? ServerStatus { get; set; }
    public required bool AllowInAutoLocation { get; set; }
    public bool IsReady => ServerState is ServerState.Idle or ServerState.Active;
    public ServerState ServerState { get; set; }
    public ServerCache UpdateState(TimeSpan lostServerThreshold)
    {
        ServerState = CalculateState(lostServerThreshold);
        return this;
    }

    private ServerState CalculateState(TimeSpan lostServerThreshold)
    {
        if (ConfigureTime == null) return ServerState.NotInstalled;
        if (ServerStatus == null || ServerStatus.CreatedTime < DateTime.UtcNow - lostServerThreshold)
            return ServerState.Lost;
        if (ConfigCode != LastConfigCode) return ServerState.Configuring;
        if (!IsEnabled) return ServerState.Disabled;
        if (ServerStatus.SessionCount == 0) return ServerState.Idle;
        return ServerState.Active;
    }
}