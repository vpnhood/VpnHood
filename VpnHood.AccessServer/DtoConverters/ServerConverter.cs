using VpnHood.AccessServer.Dtos.Servers;
using VpnHood.AccessServer.Persistence.Caches;
using VpnHood.AccessServer.Persistence.Enums;
using VpnHood.AccessServer.Persistence.Models;
using VpnHood.AccessServer.Repos.Views;
using VpnHood.Manager.Common.Utils;

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
            TotalSwapMemoryMb = model.TotalSwapMemoryMb,
            LogicalCoreCount = model.LogicalCoreCount,
            Version = model.Version,
            AutoConfigure = model.AutoConfigure,
            AccessPoints = model.AccessPoints.Select(x => x.ToDto()).ToArray(),
            HostPanelUrl = string.IsNullOrEmpty(model.HostPanelUrl) ? null : new Uri(model.HostPanelUrl),
            PublicIpV4 = model.PublicIpV4,
            PublicIpV6 = model.PublicIpV6,
            Tags = TagUtils.TagsFromString(model.Tags),
            ClientFilterId = model.ClientFilterId?.ToString(),
            ClientFilterName = model.ClientFilter?.ClientFilterName,
            ConfigSwapMemorySizeMb = model.ConfigSwapMemorySizeMb
        };
    }

    public static VpnServer ToDto(this ServerView view, ServerCache? serverCache)
    {
        var server = view.Server.ToDto(serverCache);
        server.ServerFarmName = view.ServerFarmName;
        server.ClientFilterName = view.ClientFilterName;
        return server;
    }
}