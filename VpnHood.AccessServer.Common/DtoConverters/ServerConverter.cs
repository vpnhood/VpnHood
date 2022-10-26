namespace VpnHood.AccessServer.DtoConverters;

public static class ServerConverter
{
    public static Dtos.Server FromModel(Models.Server model, TimeSpan lostServerThreshold)
    {
        return new Dtos.Server
        {
            AccessPointGroup = model.AccessPointGroup,
            AccessPointGroupId = model.AccessPointGroupId,
            AuthorizationCode = model.AuthorizationCode,
            ConfigCode = model.ConfigCode,
            ConfigureTime = model.ConfigureTime,
            CreatedTime = model.CreatedTime,
            Description = model.Description,
            EnvironmentVersion = model.EnvironmentVersion,
            IsEnabled = model.IsEnabled,
            LastConfigCode = model.LastConfigCode,
            LastConfigError = model.LastConfigError,
            LogClientIp = model.LogClientIp,
            LogLocalPort = model.LogLocalPort,
            MachineName = model.MachineName,
            OsInfo = model.OsInfo,
            ProjectId = model.ProjectId,
            ServerId = model.ServerId,
            ServerStatus = model.ServerStatus,
            ServerState = ServerUtils.ServerUtil.GetServerState(model, lostServerThreshold),
            ServerName = model.ServerName,
            TotalMemory = model.TotalMemory,
            Version = model.Version
        };
    }

}