using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Repos;
using VpnHood.AccessServer.Persistence;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers;

[Route("/api/projects/{projectId:guid}/access-point-groups")]
public class AccessPointGroupController : SuperController<AccessPointGroupController>
{
    public AccessPointGroupController(ILogger<AccessPointGroupController> logger, VhContext vhContext, MultilevelAuthRepo multilevelAuthRepo) 
        : base(logger, vhContext, multilevelAuthRepo)
    {
    }

    [HttpPost]
    public async Task<AccessPointGroup> Create(Guid projectId, AccessPointGroupCreateParams? createParams)
    {
        createParams ??= new AccessPointGroupCreateParams();
        await VerifyUserPermission(VhContext, projectId, Permissions.AccessPointGroupWrite);

        // check user quota
        using var singleRequest = SingleRequest.Start($"CreateAccessPointGroup_{CurrentUserId}");
        if (VhContext.AccessPointGroups.Count(x => x.ProjectId == projectId) >= QuotaConstants.AccessPointGroupCount)
            throw new QuotaException(nameof(VhContext.AccessPointGroups), QuotaConstants.AccessPointGroupCount);

        // create a certificate if it is not given
        Certificate certificate;
        if (createParams.CertificateId != null)
        {
            certificate = await VhContext.Certificates.SingleAsync(x => x.ProjectId == projectId && x.CertificateId == createParams.CertificateId);
        }
        else
        {
            await VerifyUserPermission(VhContext, projectId, Permissions.CertificateWrite);
            certificate = CertificateController.CreateInternal(projectId, null);
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
        var ret = new AccessPointGroup
        {
            ProjectId = projectId,
            AccessPointGroupId = id,
            AccessPointGroupName = createParams.AccessPointGroupName,
            CertificateId = certificate.CertificateId,
            CreatedTime = DateTime.UtcNow
        };

        await VhContext.AccessPointGroups.AddAsync(ret);
        await VhContext.SaveChangesAsync();
        return ret;
    }

    [HttpPatch("{accessPointGroupId}")]
    public async Task Update(Guid projectId, Guid accessPointGroupId, AccessPointGroupUpdateParams updateParams)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.AccessPointGroupWrite);

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
    }

    [HttpGet("{accessPointGroupId}")]
    public async Task<AccessPointGroup> Get(Guid projectId, Guid accessPointGroupId)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        var ret = await VhContext.AccessPointGroups
            .Include(x => x.AccessPoints)
            .SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == accessPointGroupId);

        return ret;
    }

    [HttpGet]
    public async Task<AccessPointGroup[]> List(Guid projectId, string? search = null,
        int recordIndex = 0, int recordCount = 101)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.ProjectRead);

        var ret = await VhContext.AccessPointGroups
            .Include(x => x.AccessPoints)
            .Where(x => x.ProjectId == projectId && (
                string.IsNullOrEmpty(search) ||
                x.AccessPointGroupName!.Contains(search) ||
                x.AccessPointGroupId.ToString().StartsWith(search)))
            .OrderByDescending(x=>x.CreatedTime)
            .Skip(recordIndex)
            .Take(recordCount)
            .ToArrayAsync();

        return ret;
    }


    [HttpDelete("{accessPointGroupId:guid}")]
    public async Task Delete(Guid projectId, Guid accessPointGroupId)
    {
        await VerifyUserPermission(VhContext, projectId, Permissions.AccessPointGroupWrite);

        var accessPointGroup = await VhContext.AccessPointGroups
            .SingleAsync(e => e.ProjectId == projectId && e.AccessPointGroupId == accessPointGroupId);
        VhContext.AccessPointGroups.Remove(accessPointGroup);
        await VhContext.SaveChangesAsync();
    }
}