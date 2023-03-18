using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.ServerUtils;

namespace VpnHood.AccessServer.DtoConverters;

public static class ServerConverter
{
    public static Dtos.Server ToDto(this ServerModel model, TimeSpan lostServerThreshold)
    {
        return new Dtos.Server
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
            ServerStatus = model.ServerStatus?.ToDto(),
            ServerState = ServerUtil.GetServerState(model, lostServerThreshold),
            ServerName = model.ServerName,
            TotalMemory = model.TotalMemory,
            LogicalCoreCount = model.LogicalCoreCount,
            Version = model.Version,
            AutoConfigure = model.AutoConfigure,
            AccessPoints = model.AccessPoints.Select(x => x.ToDto()).ToArray(),  
        };
    }

}