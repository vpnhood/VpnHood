using System.Net;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Utils;
using VpnHood.Common.Utils;

namespace VpnHood.AccessServer.Services;

public class ServerFarmService(
    VhContext vhContext,
    ServerService serverService,
    VhRepo vhRepo,
    CertificateService certificateService)
{
    public async Task<ServerFarm> Create(Guid projectId, ServerFarmCreateParams createParams)
    {
        // check user quota
        if (vhContext.ServerFarms.Count(x => x.ProjectId == projectId && !x.IsDeleted) >= QuotaConstants.ServerFarmCount)
            throw new QuotaException(nameof(VhContext.ServerFarms), QuotaConstants.ServerFarmCount);

        if (createParams.CertificateId == null && createParams.UseHostName)
            throw new InvalidOperationException($"To set {nameof(createParams.UseHostName)} you must set {nameof(createParams.CertificateId)}.");

        // create default name
        createParams.ServerFarmName = createParams.ServerFarmName?.Trim();
        if (string.IsNullOrWhiteSpace(createParams.ServerFarmName)) createParams.ServerFarmName = Resource.NewServerFarmTemplate;
        if (createParams.ServerFarmName.Contains("##"))
        {
            var names = await vhContext.ServerFarms
                .Where(x => x.ProjectId == projectId && !x.IsDeleted)
                .Select(x => x.ServerFarmName)
                .ToArrayAsync();

            createParams.ServerFarmName = AccessUtil.FindUniqueName(createParams.ServerFarmName, names);
        }

        // Set ServerProfileId
        var serverProfile = await vhContext.ServerProfiles
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .SingleAsync(x => (createParams.ServerProfileId == null && x.IsDefault) || x.ServerProfileId == createParams.ServerProfileId);

        // create a certificate if it is not given
        var certificate = createParams.CertificateId != null
            ? (await certificateService.Get(projectId, createParams.CertificateId.Value)).Certificate
            : (await certificateService.CreateSelfSingedInternal(projectId)).ToDto();

        var ret = new ServerFarmModel
        {
            ProjectId = projectId,
            ServerFarmId = Guid.NewGuid(),
            ServerProfileId = serverProfile.ServerProfileId,
            ServerFarmName = createParams.ServerFarmName,
            CertificateId = certificate.CertificateId,
            CreatedTime = DateTime.UtcNow,
            UseHostName = createParams.UseHostName,
            Secret = VhUtil.GenerateKey()
        };

        await vhContext.ServerFarms.AddAsync(ret);
        await vhContext.SaveChangesAsync();
        return ret.ToDto(serverProfile.ServerProfileName);
    }

    public async Task<ServerFarmData> Update(Guid projectId, Guid serverFarmId, ServerFarmUpdateParams updateParams)
    {
        var serverFarm = await vhRepo.GetServerFarm(projectId, serverFarmId, true, true);

        var reconfigure = false;

        // change other properties
        if (updateParams.ServerFarmName != null)
            serverFarm.ServerFarmName = updateParams.ServerFarmName.Value;

        if (updateParams.UseHostName != null)
            serverFarm.UseHostName = updateParams.UseHostName;

        if (updateParams.CertificateId != null && serverFarm.CertificateId != updateParams.CertificateId)
        {
            // makes sure that the certificate belongs to this project
            var certificate = await vhRepo.GetCertificate(projectId, updateParams.CertificateId.Value);
            serverFarm.CertificateId = certificate.CertificateId;
            reconfigure = true;
        }

        // Set ServerProfileId
        if (updateParams.ServerProfileId != null && updateParams.ServerProfileId != serverFarm.ServerProfileId)
        {
            // makes sure that the serverProfile belongs to this project
            var serverProfile = await vhRepo.GetServerProfile(projectId, updateParams.ServerProfileId);
            serverFarm.ServerProfileId = serverProfile.ServerProfileId;
            reconfigure = true;
        }

        // update host token
        AccessUtil.FarmTokenUpdateIfChanged(serverFarm);

        // update
        await vhRepo.SaveChangesAsync();

        // update cache after save
        if (reconfigure)
            await serverService.ReconfigServers(projectId, serverFarmId: serverFarmId);

        var ret = (await List(projectId, serverFarmId: serverFarmId)).Single();
        return ret;
    }

    public async Task<ServerFarmData[]> List(Guid projectId, string? search = null,
        Guid? serverFarmId = null,
        int recordIndex = 0, int recordCount = int.MaxValue)
    {
        var query = vhContext.ServerFarms
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => serverFarmId == null || x.ServerFarmId == serverFarmId)
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.ServerFarmName.Contains(search) ||
                x.ServerFarmId.ToString() == search)
            .Select(x => new
            {
                ServerFarm = x,
                x.ServerProfile!.ServerProfileName,
                Certificate = new CertificateModel
                {
                    CertificateId = x.CertificateId,
                    CommonName = x.Certificate!.CommonName,
                    CreatedTime = x.Certificate.CreatedTime,
                    ExpirationTime = x.Certificate.ExpirationTime
                }
            })
            .OrderByDescending(x => x.ServerFarm.ServerFarmName)
            .Skip(recordIndex)
            .Take(recordCount)
            .AsNoTracking();

        var serverFarms = await query.ToArrayAsync();
        var accessPoints = await GetPublicInTokenAccessPoints(serverFarms.Select(x => x.ServerFarm.ServerFarmId));
        var dtos = serverFarms
            .Select(x => new ServerFarmData
            {
                ServerFarm = x.ServerFarm.ToDto(x.ServerProfileName),
                Certificate = x.Certificate.ToDto(),
                AccessPoints = accessPoints.Where(y => y.ServerFarmId == x.ServerFarm.ServerFarmId)
            });

        return dtos.ToArray();
    }

    public async Task<ServerFarmAccessPoint[]> GetPublicInTokenAccessPoints(IEnumerable<Guid> farmIds)
    {
        var query = vhContext.Servers
            .Where(x => farmIds.Contains(x.ServerFarmId))
            .AsNoTracking()
            .Select(x => new
            {
                x.ServerFarmId,
                x.ServerId,
                x.ServerName,
                x.AccessPoints
            });

        var servers = await query
            .AsNoTracking()
            .ToArrayAsync();

        var items = new List<ServerFarmAccessPoint>();

        foreach (var server in servers)
            items.AddRange(server.AccessPoints
                .Where(x => x.AccessPointMode == AccessPointMode.PublicInToken)
                .Select(accessPoint => new ServerFarmAccessPoint
                {
                    ServerFarmId = server.ServerFarmId,
                    ServerId = server.ServerId,
                    ServerName = server.ServerName,
                    TcpEndPoint = new IPEndPoint(accessPoint.IpAddress, accessPoint.TcpPort)
                }));

        return items.ToArray();
    }

    public async Task<ServerFarmData[]> ListWithSummary(Guid projectId, string? search = null,
        Guid? serverFarmId = null,
        int recordIndex = 0, int recordCount = int.MaxValue)
    {
        var query = vhContext.ServerFarms
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .Where(x => serverFarmId == null || x.ServerFarmId == serverFarmId)
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.ServerFarmName.Contains(search) ||
                x.ServerFarmId.ToString().StartsWith(search))
            .Select(x => new
            {
                ServerFarm = x,
                x.ServerProfile!.ServerProfileName,
                Certificate = new CertificateModel
                {
                    CertificateId = x.CertificateId,
                    CommonName = x.Certificate!.CommonName,
                    CreatedTime = x.Certificate.CreatedTime,
                    ExpirationTime = x.Certificate.ExpirationTime
                },
                ServerCount = x.Servers!.Count(y => !y.IsDeleted),
                AccessTokens = x.AccessTokens!.Select(y => new { y.IsDeleted, y.FirstUsedTime, y.LastUsedTime }).ToArray()
            });

        // get farms
        var results = await query
            .OrderBy(x => x.ServerFarm.ServerFarmName)
            .Skip(recordIndex)
            .Take(recordCount)
            .AsNoTracking()
            .ToArrayAsync();

        // create result
        var accessPoints = await GetPublicInTokenAccessPoints(results.Select(x => x.ServerFarm.ServerFarmId));
        var now = DateTime.UtcNow;
        var ret = results.Select(x => new ServerFarmData
        {
            ServerFarm = x.ServerFarm.ToDto(x.ServerProfileName),
            Certificate = x.Certificate.ToDto(),
            AccessPoints = accessPoints.Where(y => y.ServerFarmId == x.ServerFarm.ServerFarmId),
            Summary = new ServerFarmSummary
            {
                ActiveTokenCount = x.AccessTokens.Count(accessToken => !accessToken.IsDeleted && accessToken.LastUsedTime >= now.AddDays(-7)),
                InactiveTokenCount = x.AccessTokens.Count(accessToken => !accessToken.IsDeleted && accessToken.LastUsedTime < now.AddDays(-7)),
                UnusedTokenCount = x.AccessTokens.Count(accessToken => !accessToken.IsDeleted && accessToken.FirstUsedTime == null),
                TotalTokenCount = x.AccessTokens.Count(accessToken => !accessToken.IsDeleted),
                ServerCount = x.ServerCount
            }
        });

        return ret.ToArray();
    }

    public async Task Delete(Guid projectId, Guid serverFarmId)
    {
        var serverFarm = await vhContext.ServerFarms
            .Include(x => x.Servers!.Where(y => !y.IsDeleted))
            .Include(x => x.AccessTokens)
            .Where(x => x.ProjectId == projectId && !x.IsDeleted)
            .SingleAsync(x => x.ProjectId == projectId && x.ServerFarmId == serverFarmId);

        if (serverFarm.Servers!.Any())
            throw new InvalidOperationException("A farm with a server can not be deleted.");

        serverFarm.IsDeleted = true;
        foreach (var accessToken in serverFarm.AccessTokens!)
            accessToken.IsDeleted = true;

        await vhContext.SaveChangesAsync();
    }
}