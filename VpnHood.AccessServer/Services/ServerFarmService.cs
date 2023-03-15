using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Controllers;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos.ServerFarmDtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Services;

public class ServerFarmService
{
    private readonly VhContext _vhContext;
    private readonly ServerService _serverService;

    public ServerFarmService(
        VhContext vhContext,
        ServerService serverService)
    {
        _vhContext = vhContext;
        _serverService = serverService;
    }
    public async Task<ServerFarm> Create(Guid projectId, ServerFarmCreateParams createParams)
    {
        // check user quota
        if (_vhContext.ServerFarms.Count(x => x.ProjectId == projectId && !x.IsDeleted) >= QuotaConstants.ServerFarmCount)
            throw new QuotaException(nameof(VhContext.ServerFarms), QuotaConstants.ServerFarmCount);

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
        createParams.ServerFarmName = createParams.ServerFarmName?.Trim();
        if (string.IsNullOrWhiteSpace(createParams.ServerFarmName)) createParams.ServerFarmName = Resource.NewServerFarmTemplate;
        if (createParams.ServerFarmName.Contains("##"))
        {
            var names = await _vhContext.ServerFarms
                .Where(x => x.ProjectId == projectId && !x.IsDeleted)
                .Select(x => x.ServerFarmName)
                .ToArrayAsync();

            createParams.ServerFarmName = AccessUtil.FindUniqueName(createParams.ServerFarmName, names);
        }

        // Set ServerProfileId
        var serverProfile = await _vhContext.ServerProfiles
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .SingleAsync(x => (createParams.ServerProfileId == null && x.IsDefault) || x.ServerProfileId == createParams.ServerProfileId);

        var id = Guid.NewGuid();
        var ret = new ServerFarmModel
        {
            ProjectId = projectId,
            ServerFarmId = id,
            ServerProfileId = serverProfile.ServerProfileId,
            ServerFarmName = createParams.ServerFarmName,
            CertificateId = certificate.CertificateId,
            CreatedTime = DateTime.UtcNow
        };

        await _vhContext.ServerFarms.AddAsync(ret);
        await _vhContext.SaveChangesAsync();
        return ret.ToDto();
    }

    public async Task<ServerFarm> Update(Guid projectId, Guid serverFarmId, ServerFarmUpdateParams updateParams)
    {
        var serverFarm = await _vhContext.ServerFarms
            .Include(x => x.ServerProfile)
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .SingleAsync(x => x.ServerFarmId == serverFarmId);

        // check createParams.CertificateId access
        var certificate = updateParams.CertificateId != null
            ? await _vhContext.Certificates.SingleAsync(x => x.ProjectId == projectId && x.CertificateId == updateParams.CertificateId)
            : null;

        // change other properties
        if (updateParams.ServerFarmName != null)
            serverFarm.ServerFarmName = updateParams.ServerFarmName.Value;

        if (certificate != null)
            serverFarm.CertificateId = certificate.CertificateId;

        // Set ServerProfileId
        var isServerProfileChanged = updateParams.ServerProfileId != null && updateParams.ServerProfileId != serverFarm.ServerProfileId;
        if (isServerProfileChanged)
        {
            var serverProfile = await _vhContext.ServerProfiles
                .Where(x => x.ProjectId == projectId && !x.IsDeleted)
                .SingleAsync(x => x.ServerProfileId == updateParams.ServerProfileId!);

            serverFarm.ServerProfileId = serverProfile.ServerProfileId;
        }

        // update
        _vhContext.ServerFarms.Update(serverFarm);
        await _vhContext.SaveChangesAsync();

        // update cache after save
        if (isServerProfileChanged)
            await _serverService.ReconfigServers(projectId, serverFarmId: serverFarmId);

        return serverFarm.ToDto();
    }

    public async Task<ServerFarmData[]> List(Guid projectId, string? search = null,
        Guid? serverFarmId = null,
        int recordIndex = 0, int recordCount = int.MaxValue)
    {
        var query = _vhContext.ServerFarms
            .Include(x => x.ServerProfile)
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => serverFarmId == null || x.ServerFarmId == serverFarmId)
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.ServerFarmName.Contains(search) ||
                x.ServerFarmId.ToString().StartsWith(search))
            .OrderByDescending(x => x.ServerFarmName);

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
        var now = DateTime.UtcNow;
        var query = _vhContext.ServerFarms
            .Include(x => x.ServerProfile)
            .Include(x => x.Servers)
            .Include(x => x.AccessTokens)
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => serverFarmId == null || x.ServerFarmId == serverFarmId)
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.ServerFarmName.Contains(search) ||
                x.ServerFarmId.ToString().StartsWith(search))
            .OrderBy(x => x.ServerFarmName)
            .Select(x => new ServerFarmData
            {
                ServerFarm = x.ToDto(),
                Summary = new ServerFarmSummary
                {
                    ActiveTokenCount = x.AccessTokens!.Count(accessToken => !accessToken.IsDeleted && accessToken.LastUsedTime >= now.AddDays(-7)),
                    InactiveTokenCount = x.AccessTokens!.Count(accessToken => !accessToken.IsDeleted && accessToken.LastUsedTime < now.AddDays(-7)),
                    UnusedTokenCount = x.AccessTokens!.Count(accessToken => !accessToken.IsDeleted && accessToken.FirstUsedTime == null),
                    TotalTokenCount = x.AccessTokens!.Count(accessToken => !accessToken.IsDeleted),
                    ServerCount = x.Servers!.Count(server => !server.IsDeleted)
                }
            });

        // get farms
        var serverFarms = await query
            .Skip(recordIndex)
            .Take(recordCount)
            .AsNoTracking()
            .ToArrayAsync();

        return serverFarms.ToArray();
    }

    public async Task Delete(Guid projectId, Guid serverFarmId)
    {
        var serverFarm = await _vhContext.ServerFarms
            .Include(x => x.Servers!.Where(y => !y.IsDeleted))
            .Include(x => x.AccessTokens)
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .SingleAsync(x => x.ProjectId == projectId && x.ServerFarmId == serverFarmId);

        if (serverFarm.Servers!.Any())
            throw new InvalidOperationException("A farm with a server can not be deleted.");

        serverFarm.IsDeleted = true;
        foreach (var accessToken in serverFarm.AccessTokens!)
            accessToken.IsDeleted = true;

        await _vhContext.SaveChangesAsync();
    }
}