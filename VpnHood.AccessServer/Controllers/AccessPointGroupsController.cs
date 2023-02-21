﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Services;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/v{version:apiVersion}/projects/{projectId:guid}/access-point-groups")]
public class AccessPointGroupsController : SuperController<AccessPointGroupsController>
{
    public AccessPointGroupsController(ILogger<AccessPointGroupsController> logger, VhContext vhContext, MultilevelAuthService multilevelAuthService)
        : base(logger, vhContext, multilevelAuthService)
    {
    }

    [HttpPost]
    public async Task<AccessPointGroup> Create(Guid projectId, AccessPointGroupCreateParams? createParams)
    {
        createParams ??= new AccessPointGroupCreateParams();
        await VerifyUserPermission(projectId, Permissions.AccessPointGroupWrite);

        // check user quota
        using var singleRequest = SingleRequest.Start($"CreateAccessPointGroup_{CurrentUserId}");
        if (VhContext.AccessPointGroups.Count(x => x.ProjectId == projectId) >= QuotaConstants.AccessPointGroupCount)
            throw new QuotaException(nameof(VhContext.AccessPointGroups), QuotaConstants.AccessPointGroupCount);

        // create a certificate if it is not given
        CertificateModel certificate;
        if (createParams.CertificateId != null)
        {
            certificate = await VhContext.Certificates.SingleAsync(x => x.ProjectId == projectId && x.CertificateId == createParams.CertificateId);
        }
        else
        {
            await VerifyUserPermission(projectId, Permissions.CertificateWrite);
            certificate = CertificatesController.CreateInternal(projectId, null);
            VhContext.Certificates.Add(certificate);
        }

        // create default name
        createParams.AccessPointGroupName = createParams.AccessPointGroupName?.Trim();
        if (string.IsNullOrWhiteSpace(createParams.AccessPointGroupName)) createParams.AccessPointGroupName = Resource.NewServerFarmTemplate;
        if (createParams.AccessPointGroupName.Contains("##"))
        {
            var names = await VhContext.AccessPointGroups
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

        await VhContext.AccessPointGroups.AddAsync(ret);
        await VhContext.SaveChangesAsync();
        return ret.ToDto();
    }

    [HttpPatch("{accessPointGroupId}")]
    public async Task<AccessPointGroup> Update(Guid projectId, Guid accessPointGroupId, AccessPointGroupUpdateParams updateParams)
    {
        await VerifyUserPermission(projectId, Permissions.AccessPointGroupWrite);

        var accessPointGroup = await VhContext.AccessPointGroups.SingleAsync(x =>
            x.ProjectId == projectId && x.AccessPointGroupId == accessPointGroupId);

        // check createParams.CertificateId access
        var certificate = updateParams.CertificateId != null
            ? await VhContext.Certificates.SingleAsync(x => x.ProjectId == projectId && x.CertificateId == updateParams.CertificateId)
            : null;

        // change other properties
        if (updateParams.AccessPointGroupName != null) accessPointGroup.AccessPointGroupName = updateParams.AccessPointGroupName.Value;
        if (certificate != null) accessPointGroup.CertificateId = certificate.CertificateId;

        // update
        VhContext.AccessPointGroups.Update(accessPointGroup);
        await VhContext.SaveChangesAsync();

        return accessPointGroup.ToDto();
    }

    [HttpGet("{accessPointGroupId}")]
    public async Task<ServerFarmData> Get(Guid projectId, Guid accessPointGroupId)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);

        var serverFarm = await VhContext.AccessPointGroups
            .Include(x => x.Servers)
            .Include(x => x.AccessPoints)
            .SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == accessPointGroupId);

        var ret = new ServerFarmData
        {
            ServerFarm = serverFarm.ToDto(),
            ServerCount = serverFarm.Servers!.Count
        };

        return ret;
    }

    [HttpGet]
    public async Task<ServerFarmData[]> List(Guid projectId, string? search = null,
        int recordIndex = 0, int recordCount = 101)
    {
        await VerifyUserPermission(projectId, Permissions.ProjectRead);

        var query = VhContext.AccessPointGroups
            .Include(x => x.AccessPoints)
            .Include(x => x.Servers)
            .Where(x => x.ProjectId == projectId)
            .Where(x =>
                string.IsNullOrEmpty(search) ||
                x.AccessPointGroupName!.Contains(search) ||
                x.AccessPointGroupId.ToString().StartsWith(search))
            .OrderByDescending(x => x.CreatedTime);

        var serverFarms = await query
            .Skip(recordIndex)
            .Take(recordCount)
            .ToArrayAsync();

        var ret = serverFarms
            .Select(serverFarm => new ServerFarmData
            {
                ServerFarm = serverFarm.ToDto(),
                ServerCount = serverFarm.Servers!.Count
            });

        return ret.ToArray();
    }


    [HttpDelete("{accessPointGroupId:guid}")]
    public async Task Delete(Guid projectId, Guid accessPointGroupId)
    {
        await VerifyUserPermission(projectId, Permissions.AccessPointGroupWrite);

        var accessPointGroup = await VhContext.AccessPointGroups
            .SingleAsync(e => e.ProjectId == projectId && e.AccessPointGroupId == accessPointGroupId);
        VhContext.AccessPointGroups.Remove(accessPointGroup);
        await VhContext.SaveChangesAsync();
    }
}