using VpnHood.AccessServer.Dtos.Servers;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class ServerConverter
{
    public static VpnServer ToDto(this ServerModel model, ServerCache? serverCache)
    {
        var serverState = model.ConfigureTime == null ? ServerState.NotInstalled : ServerState.Lost;

        return new VpnServer {
            ServerFarmId = model.ServerFarmId,
            ServerFarmName = model.ServerFarm?.ServerFarmName,
            Location = model.Location?.ToDto(),
            AllowInAutoLocation = model.AllowInAutoLocation,
            ConfigureTime = model.ConfigureTime,
            CreatedTime = model.CreatedTime,
            Description = model.Description,
            EnvironmentVersion = model.EnvironmentVersion,
            IsEnabled = model.IsEnabled,
            LastConfigError = model.LastConfigError,
            MachineName = model.MachineName,
            OsInfo = model.OsInfo,
            Power = model.Power,
            ServerId = model.ServerId,
            ServerStatus = serverCache?.ServerStatus?.ToDto(),
            ServerState = serverCache?.ServerState ?? serverState,
            ServerName = model.ServerName,
            TotalMemory = model.TotalMemory,
            LogicalCoreCount = model.LogicalCoreCount,
            Version = model.Version,
            AutoConfigure = model.AutoConfigure,
            AccessPoints = model.AccessPoints.Select(x => x.ToDto()).ToArray(),
            HostPanelUrl = string.IsNullOrEmpty(model.HostPanelUrl) ? null : new Uri(model.HostPanelUrl),
        };
    }
}