﻿using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Persistence;
using VpnHood.Common.Utils;
using VpnHood.Server.Access.Configurations;

namespace VpnHood.AccessServer.Services;

public class ServerProfileService
{
    private readonly VhContext _vhContext;
    private readonly ServerService _serverService;

    public ServerProfileService(
        VhContext vhContext,
        ServerService serverService)
    {
        _vhContext = vhContext;
        _serverService = serverService;
    }

    public async Task<ServerProfile> Create(Guid projectId, ServerProfileCreateParams? createParams = null)
    {
        // create default name
        createParams ??= new ServerProfileCreateParams();
        var serverConfig = ServerConfig_FromJson(createParams.ServerConfig);
        createParams.ServerProfileName = createParams.ServerProfileName?.Trim();
        if (string.IsNullOrWhiteSpace(createParams.ServerProfileName)) createParams.ServerProfileName = Resource.NewServerProfileTemplate;
        if (createParams.ServerProfileName.Contains("##"))
        {
            var names = await _vhContext.ServerProfiles
                .Where(x => x.ProjectId == projectId && !x.IsDeleted)
                .Select(x => x.ServerProfileName)
                .ToArrayAsync();

            createParams.ServerProfileName = AccessServerUtil.FindUniqueName(createParams.ServerProfileName, names);
        }

        var serverProfile = new ServerProfileModel
        {
            ServerProfileId = Guid.NewGuid(),
            ServerProfileName = createParams.ServerProfileName,
            ProjectId = projectId,
            ServerConfig = ServerConfig_ToJson(serverConfig),
            IsDefault = false,
            CreatedTime = DateTime.UtcNow
        };

        await _vhContext.ServerProfiles.AddAsync(serverProfile);
        await _vhContext.SaveChangesAsync();
        return serverProfile.ToDto();
    }

    public async Task<ServerProfile> Update(Guid projectId, Guid serverProfileId, ServerProfileUpdateParams updateParams)
    {
        var model = await _vhContext.ServerProfiles
            .Include(x => x.ServerFarms)
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .SingleAsync(x => x.ServerProfileId == serverProfileId);

        if (updateParams.ServerProfileName != null)
        {
            if (model.IsDefault)
                throw new InvalidOperationException("The default Server Profile cannot be renamed.");
            model.ServerProfileName = updateParams.ServerProfileName;
        }

        if (updateParams.ServerConfig != null)
        {
            var serveConfig = ServerConfig_FromJson(updateParams.ServerConfig.Value);
            model.ServerConfig = ServerConfig_ToJson(serveConfig);
        }

        // save
        await _vhContext.SaveChangesAsync();

        // update cache after save
        await _serverService.ReconfigServers(projectId, serverProfileId: serverProfileId);

        return model.ToDto();
    }

    public async Task Delete(Guid projectId, Guid serverProfileId)
    {
        var model = await _vhContext.ServerProfiles
            .Include(x => x.ServerFarms)
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .SingleAsync(x => x.ServerProfileId == serverProfileId);

        if (model.ServerFarms!.Any())
            throw new InvalidOperationException("The profile is using by a ServerFarm.");

        if (model.IsDefault)
            throw new InvalidOperationException("The default Server Profile can not be deleted.");

        model.IsDeleted = true;
        await _vhContext.SaveChangesAsync();
    }

    public async Task<ServerProfileData[]> ListWithSummary(Guid projectId, string? search = null,
        bool includeSummary = false,
        Guid? serverProfileId = null, int recordIndex = 0, int recordCount = int.MaxValue)
    {
        _ = includeSummary; //not used yet

        var query = _vhContext.ServerProfiles
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => serverProfileId == null || x.ServerProfileId == serverProfileId)
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.ServerProfileName.Contains(search) ||
                x.ServerProfileId.ToString() == search)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.ServerProfileName)
            .Select(x => new ServerProfileData
            {
                ServerProfile = x.ToDto(),
            });

        // get farms
        var serverProfileDatas = await query
            .Skip(recordIndex)
            .Take(recordCount)
            .AsNoTracking()
            .ToArrayAsync();

        if (includeSummary )
        {
            var summariesQuery =
                from serverFarm in _vhContext.ServerFarms
                    .Where(x => x.ProjectId == projectId && !x.IsDeleted)
                    .Where(x => serverProfileId == null || x.ServerProfileId == serverProfileId)
                join server in _vhContext.Servers on serverFarm.ServerFarmId equals server.ServerFarmId into grouping
                from server in grouping.DefaultIfEmpty()
                select new { serverFarm.ServerProfileId, serverFarm.ServerFarmId, ServerId = (Guid?)server.ServerId };
                

            // update summaries
            var summaries = await summariesQuery.ToArrayAsync();
            foreach (var serverProfileData in serverProfileDatas)
            {
                serverProfileData.Summary = new ServerProfileSummary
                {
                    ServerCount = summaries.Count(x => x.ServerId != null),
                    ServerFarmCount = summaries.DistinctBy(x => x.ServerFarmId).Count(),
                };
            }
        }

        return serverProfileDatas.ToArray();
    }

    private static string? ServerConfig_ToJson(ServerConfig? serverConfig)
    {
        if (serverConfig == null) return null;
        var serverConfigJson = JsonSerializer.Serialize(serverConfig);

        if (serverConfigJson.Length > 0xFFFF)
            throw new Exception("ServerConfig is too big");

        return JsonSerializer.Serialize(new ServerConfig()) == serverConfigJson ? null : serverConfigJson;
    }

    private static ServerConfig? ServerConfig_FromJson(string? serverConfigJson)
    {
        if (string.IsNullOrEmpty(serverConfigJson))
            return null;

        var serverConfig = VhUtil.JsonDeserialize<ServerConfig>(serverConfigJson);

        if (!string.IsNullOrEmpty(serverConfig.ConfigCode))
            throw new ArgumentException($"You can not set {nameof(serverConfig.ConfigCode)}.", nameof(serverConfig));

        if (!VhUtil.IsNullOrEmpty(serverConfig.TcpEndPoints))
            throw new ArgumentException($"You can not set {nameof(serverConfig.TcpEndPoints)}.", nameof(serverConfig));

        if (serverConfig.SessionOptions.SyncInterval != null)
            throw new ArgumentException($"You can not set {nameof(serverConfig.SessionOptions.SyncInterval)}.", nameof(serverConfig));

        if (serverConfig.SessionOptions.SyncCacheSize != null)
            throw new ArgumentException($"You can not set {nameof(serverConfig.SessionOptions.SyncCacheSize)}.", nameof(serverConfig));

        if (serverConfig.SessionOptions.Timeout != null)
            throw new ArgumentException($"You can not set {nameof(serverConfig.SessionOptions.Timeout)}.", nameof(serverConfig));

        if (serverConfig.UpdateStatusInterval != null)
            throw new ArgumentException($"You can not set {nameof(serverConfig.UpdateStatusInterval)}.", nameof(serverConfig));

        if (!string.IsNullOrEmpty(serverConfig.ConfigCode))
            throw new ArgumentException($"You can not set {nameof(serverConfig.ConfigCode)}.", nameof(serverConfig));

        if (serverConfig.SessionOptions.TcpBufferSize < 0x1000)
            throw new ArgumentException($"You can not set {nameof(serverConfig.ConfigCode)} smaller than {0x1000}.", nameof(serverConfig));

        if (serverConfig.ServerSecret != null)
            throw new ArgumentException($"You can not set {nameof(serverConfig.ServerSecret)} here.", nameof(serverConfig));

        return serverConfig;
    }
}