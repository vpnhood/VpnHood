namespace VpnHood.AccessServer.DtoConverters;

public static class ServerConverter
{
    public static Dtos.Server ToDto(this Models.Server model, TimeSpan lostServerThreshold)
    {
        return new Dtos.Server
        {
            AccessPointGroup = model.AccessPointGroup,
            AccessPointGroupId = model.AccessPointGroupId,
            ConfigureTime = model.ConfigureTime,
            CreatedTime = model.CreatedTime,
            Description = model.Description,
            EnvironmentVersion = model.EnvironmentVersion,
            IsEnabled = model.IsEnabled,
            LastConfigError = model.LastConfigError,
            LogClientIp = model.LogClientIp,
            LogLocalPort = model.LogLocalPort,
            MachineName = model.MachineName,
            OsInfo = model.OsInfo,
            ServerId = model.ServerId,
            ServerStatus = model.ServerStatus?.ToDto(),
            ServerState = ServerUtils.ServerUtil.GetServerState(model, lostServerThreshold),
            ServerName = model.ServerName,
            TotalMemory = model.TotalMemory,
            Version = model.Version
        };
    }

}