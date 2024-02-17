using VpnHood.AccessServer.Dtos.Server;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Persistence.Utils;

namespace VpnHood.AccessServer.DtoConverters;

public static class ServerConverter
{
    public static VpnServer ToDto(this ServerModel model, ServerCache? serverCached)
    {
        var serverState = model.ConfigureTime == null ? ServerState.NotInstalled : ServerState.Lost;

        return new VpnServer
        {
            ServerFarmId = model.ServerFarmId,
            ServerFarmName = model.ServerFarm?.ServerFarmName,
            ConfigureTime = model.ConfigureTime,
            CreatedTime = model.CreatedTime,
            Description = model.Description,
            EnvironmentVersion = model.EnvironmentVersion,
            IsEnabled = model.IsEnabled,
            LastConfigError = model.LastConfigError,
            MachineName = model.MachineName,
            OsInfo = model.OsInfo,
            ServerId = model.ServerId,
            ServerStatus = serverCached?.ServerStatus?.ToDto(),
            ServerState = serverCached?.ServerState ?? serverState,
            ServerName = model.ServerName,
            TotalMemory = model.TotalMemory,
            LogicalCoreCount = model.LogicalCoreCount,
            Version = model.Version,
            AutoConfigure = model.AutoConfigure,
            AccessPoints = model.AccessPoints.Select(x => x.ToDto()).ToArray()
        };
    }

}