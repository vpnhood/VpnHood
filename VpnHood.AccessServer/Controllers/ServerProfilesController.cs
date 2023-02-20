using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;
using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VpnHood.Common.Utils;
using VpnHood.Server.Configurations;

namespace VpnHood.AccessServer.Controllers;


[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/server-profiles")]
public class ServerProfilesController : SuperController<ServerProfilesController>
{
    public ServerProfilesController(
        ILogger<ServerProfilesController> logger,
        VhContext vhContext,
        MultilevelAuthService multilevelAuthService)
        : base(logger, vhContext, multilevelAuthService)
    {
    }

    [HttpPost]
    public async Task<ServerProfile> Create(Guid projectId, ServerProfileCreateParams? createParams = null)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectWrite);
        createParams ??= new ServerProfileCreateParams();
        var serverConfig = ServerConfig_FromJson(createParams.ServerConfig);

        var serverProfileModel = new ServerProfileModel
        {
            ServerProfileId = Guid.NewGuid(),
            ProjectId = projectId,
            ServerConfig = ServerConfig_ToJson(serverConfig)
        };

        await VhContext.ServerProfiles.AddAsync(serverProfileModel);
        await VhContext.SaveChangesAsync();
        return serverProfileModel.ToDto();
    }

    [HttpGet("{serverProfileId:guid}")]
    public async Task<ServerProfile> Get(Guid projectId, Guid serverProfileId)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);

        var model = await VhContext.ServerProfiles
            .Where(x => x.ProjectId == projectId)
            .SingleAsync(x => x.ServerProfileId == serverProfileId);
        return model.ToDto();
    }

    [HttpPatch("{serverProfileId:guid}")]
    public async Task<ServerProfile> Update(Guid projectId, Guid serverProfileId, ServerProfileUpdateParams updateParams)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectWrite);

        var model = await VhContext.ServerProfiles
            .Where(x => x.ProjectId == projectId)
            .SingleAsync(x => x.ServerProfileId == serverProfileId);

        if (updateParams.ServerConfig != null)
        {
            var serveConfig = ServerConfig_FromJson(updateParams.ServerConfig.Value);
            model.ServerConfig = ServerConfig_ToJson(serveConfig);
        }

        // todo: reconfig all servers

        await VhContext.SaveChangesAsync();
        return model.ToDto();
    }


    [HttpDelete("{serverProfileId:guid}")]
    public async Task Delete(Guid projectId, Guid serverProfileId)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectWrite);

        var model = await VhContext.ServerProfiles
            .Where(x => x.ProjectId == projectId)
            .SingleAsync(x => x.ServerProfileId == serverProfileId);

        VhContext.Remove(model);
        await VhContext.SaveChangesAsync();
    }

    private static string? ServerConfig_ToJson(ServerConfig? serverConfig)
    {
        var res = serverConfig != null ? JsonSerializer.Serialize(serverConfig) : null;
        
        if (res?.Length > 0xFFFF)
            throw new Exception("ServerConfig is too big");

        return res;
    }

    private static ServerConfig? ServerConfig_FromJson(string? serverConfigJson)
    {
        if (serverConfigJson == null)
            return null;

        var serverConfig = Util.JsonDeserialize<ServerConfig>(serverConfigJson);

        if (!string.IsNullOrEmpty(serverConfig.ConfigCode))
            throw new ArgumentException($"You can not set {nameof(serverConfig.ConfigCode)}.", nameof(serverConfig));

        if (!Util.IsNullOrEmpty(serverConfig.TcpEndPoints))
            throw new ArgumentException($"You can not set {nameof(serverConfig.TcpEndPoints)}.", nameof(serverConfig));

        if (serverConfig.SessionOptions.SyncInterval!=null)
            throw new ArgumentException($"You can not set {nameof(serverConfig.SessionOptions.SyncInterval)}.", nameof(serverConfig));

        if (serverConfig.SessionOptions.SyncCacheSize < 100 * 1000000)
            throw new ArgumentException($"You can not set {nameof(serverConfig.SessionOptions.SyncInterval)} less than 100 MB");

        if (serverConfig.UpdateStatusInterval != null && serverConfig.UpdateStatusInterval < TimeSpan.FromSeconds(60))
            throw new ArgumentException($"You can not set {nameof(serverConfig.UpdateStatusInterval)} less than 60 seconds.", nameof(serverConfig));

        return serverConfig;
    }

}