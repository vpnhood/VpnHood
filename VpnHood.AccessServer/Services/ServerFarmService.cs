using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NLog;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Services;

public class ServerFarmService
{
    private readonly VhContext _vhContext;
    private readonly ServerService _serverService;
    private readonly ILogger<ServerFarmService> _logger;

    public ServerFarmService(
        VhContext vhContext,
        ServerService serverService,
        ILogger<ServerFarmService> logger)
    {
        _vhContext = vhContext;
        _serverService = serverService;
        _logger = logger;
    }
    public async Task<AccessPointGroup> Create(Guid projectId, AccessPointGroupCreateParams createParams)
    {
        // check user quota
        if (_vhContext.AccessPointGroups.Count(x => x.ProjectId == projectId) >= QuotaConstants.AccessPointGroupCount)
            throw new QuotaException(nameof(VhContext.AccessPointGroups), QuotaConstants.AccessPointGroupCount);

        // create a certificate if it is not given
        CertificateModel certificate;
        if (createParams.CertificateId != null)
        {
            certificate = await _vhContext.Certificates.SingleAsync(x => x.ProjectId == projectId && x.CertificateId == createParams.CertificateId);
        }
        else
        {
            certificate = CertificatesController.CreateInternal(projectId, null);
            _vhContext.Certificates.Add(certificate);
        }

        // create default name
        createParams.AccessPointGroupName = createParams.AccessPointGroupName?.Trim();
        if (string.IsNullOrWhiteSpace(createParams.AccessPointGroupName)) createParams.AccessPointGroupName = Resource.NewServerFarmTemplate;
        if (createParams.AccessPointGroupName.Contains("##"))
        {
            var names = await _vhContext.AccessPointGroups
                .Where(x => x.ProjectId == projectId)
                .Select(x => x.AccessPointGroupName)
                .ToArrayAsync();

            createParams.AccessPointGroupName = AccessUtil.FindUniqueName(createParams.AccessPointGroupName, names);
        }

        var id = Guid.NewGuid();
        var ret = new AccessPointGroupModel
        {
            ProjectId = projectId,
            AccessPointGroupId = id,
            AccessPointGroupName = createParams.AccessPointGroupName,
            CertificateId = certificate.CertificateId,
            CreatedTime = DateTime.UtcNow
        };

        await _vhContext.AccessPointGroups.AddAsync(ret);
        await _vhContext.SaveChangesAsync();
        return ret.ToDto();
    }

    public async Task<AccessPointGroup> Update(Guid projectId, Guid accessPointGroupId, AccessPointGroupUpdateParams updateParams)
    {
        var accessPointGroup = await _vhContext.AccessPointGroups
            .Where(x => x.ProjectId == projectId )
            .SingleAsync(x => x.AccessPointGroupId == accessPointGroupId);

        // check createParams.CertificateId access
        var certificate = updateParams.CertificateId != null
            ? await _vhContext.Certificates.SingleAsync(x => x.ProjectId == projectId && x.CertificateId == updateParams.CertificateId)
            : null;

        // change other properties
        if (updateParams.AccessPointGroupName != null)
            accessPointGroup.AccessPointGroupName = updateParams.AccessPointGroupName.Value;

        if (certificate != null)
            accessPointGroup.CertificateId = certificate.CertificateId;

        // update
        _vhContext.AccessPointGroups.Update(accessPointGroup);
        await _vhContext.SaveChangesAsync();

        return accessPointGroup.ToDto();
    }


    public async Task<ServerFarmData[]> List(Guid projectId, string? search = null,
        Guid? serverFarmId = null,
        int recordIndex = 0, int recordCount = int.MaxValue)
    {
        var query = _vhContext.AccessPointGroups
            .Where(x => x.ProjectId == projectId)
            .Where(x => serverFarmId == null || x.AccessPointGroupId == serverFarmId)
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.AccessPointGroupName!.Contains(search) ||
                x.AccessPointGroupId.ToString().StartsWith(search))
            .OrderByDescending(x => x.CreatedTime);

        var serverFarms = await query
            .Skip(recordIndex)
            .Take(recordCount)
            .AsNoTracking()
            .ToArrayAsync();

        var dtos = serverFarms
            .Select(serverFarm => new ServerFarmData
            {
                ServerFarm = serverFarm.ToDto(),
            });

        return dtos.ToArray();
    }


    public async Task<ServerFarmData[]> ListWithSummary(Guid projectId, string? search = null,
        Guid? serverFarmId = null,
        int recordIndex = 0, int recordCount = int.MaxValue)
    {
        var query = _vhContext.AccessPointGroups
            .Include(x => x.AccessTokens)
            .Where(x => x.ProjectId == projectId )
            .Where(x => serverFarmId == null || x.AccessPointGroupId == serverFarmId)
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.AccessPointGroupName!.Contains(search) ||
                x.AccessPointGroupId.ToString().StartsWith(search))
            .OrderByDescending(x => x.CreatedTime);

        // get farms
        var serverFarms = await query
            .Skip(recordIndex)
            .Take(recordCount)
            .AsNoTracking()
            .ToArrayAsync();

        // get server data
        var serverDatas = await _serverService.List(projectId);

        var now = DateTime.UtcNow;
        var ret = serverFarms.Select(serverFarm => new ServerFarmData
        {
            ServerFarm = serverFarm.ToDto(),
            Summary = new ServerFarmSummary
            {
                SessionCount = serverDatas
                    .Where(serverData => serverData.Server.AccessPointGroupId == serverFarm.AccessPointGroupId)
                    .Sum(serverData => serverData.Server.ServerStatus?.SessionCount ?? 0),

                TransferSpeed = serverDatas
                    .Where(serverData => serverData.Server.AccessPointGroupId == serverFarm.AccessPointGroupId)
                    .Sum(serverData =>
                        serverData.Server.ServerStatus?.TunnelReceiveSpeed ?? 0 +
                        serverData.Server.ServerStatus?.TunnelSendSpeed ?? 0),

                ServerCount = serverDatas
                    .Count(serverData => serverData.Server.AccessPointGroupId == serverFarm.AccessPointGroupId),

                ActiveTokenCount = serverFarm.AccessTokens!.Count(x => x.LastUsedTime >= now.AddDays(-7)),
                InactiveTokenCount = serverFarm.AccessTokens!.Count(x => x.LastUsedTime < now.AddDays(-7)),
                UnusedTokenCount = serverFarm.AccessTokens!.Count(x => x.FirstUsedTime == null),
                TotalTokenCount = serverFarm.AccessTokens!.Count
            }
        });

        return ret.ToArray();
    }

    public async Task Delete(Guid projectId, Guid serverFarmId)
    {
        var serverFarm = await _vhContext.AccessPointGroups
            .Include(x => x.Servers)
            .SingleAsync(e => e.ProjectId == projectId && e.AccessPointGroupId == serverFarmId);

        if (serverFarm.Servers!.Any(x => !x.IsDeleted))
            throw new InvalidOperationException("A farm with a server can not be deleted.");

        _vhContext.Remove(serverFarm);
        await _vhContext.SaveChangesAsync();
    }
}