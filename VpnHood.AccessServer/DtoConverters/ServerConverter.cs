using VpnHood.AccessServer.Dtos.Server;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Persistence.Utils;

namespace VpnHood.AccessServer.DtoConverters;

public static class ServerConverter
{
    public static VpnServer ToDto(this ServerModel model, ServerCache? serverCached, TimeSpan lostServerThreshold)
    {
        var stateResolver = new ServerStateResolver
        {
            ServerStatus = serverCached?.ServerStatus,
            LostServerThreshold = lostServerThreshold, 
            ConfigureTime = model.ConfigureTime,
            ConfigCode = model.ConfigCode,
            LastConfigCode = model.LastConfigCode,
            IsEnabled = model.IsEnabled
        };

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
            ServerState = stateResolver.State,
            ServerName = model.ServerName,
            TotalMemory = model.TotalMemory,
            LogicalCoreCount = model.LogicalCoreCount,
            Version = model.Version,
            AutoConfigure = model.AutoConfigure,
            AccessPoints = model.AccessPoints.Select(x => x.ToDto()).ToArray()
        };
    }

}