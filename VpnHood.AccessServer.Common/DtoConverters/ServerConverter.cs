using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.ServerUtils;

namespace VpnHood.AccessServer.DtoConverters;

public static class ServerConverter
{
    public static Dtos.Server ToDto(this ServerModel model, 
        string? accessPointGroupName,
        ServerStatusEx? serverStatus,
        TimeSpan lostServerThreshold)
    {
        return new Dtos.Server
        {
            AccessPointGroupId = model.AccessPointGroupId,
            AccessPointGroupName = accessPointGroupName,
            ConfigureTime = model.ConfigureTime,
            CreatedTime = model.CreatedTime,
            Description = model.Description,
            EnvironmentVersion = model.EnvironmentVersion,
            IsEnabled = model.IsEnabled,
            LastConfigError = model.LastConfigError,
            MachineName = model.MachineName,
            OsInfo = model.OsInfo,
            ServerId = model.ServerId,
            ServerStatus = serverStatus,
            ServerState = ServerUtil.GetServerState(model, lostServerThreshold),
            ServerName = model.ServerName,
            TotalMemory = model.TotalMemory,
            LogicalCoreCount = model.LogicalCoreCount,
            Version = model.Version
        };
    }

}