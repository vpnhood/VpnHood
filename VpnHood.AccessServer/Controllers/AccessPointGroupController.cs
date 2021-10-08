using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId:guid}/access-point-groups")]
    public class AccessPointGroupController : SuperController<AccessPointGroupController>
    {
        public AccessPointGroupController(ILogger<AccessPointGroupController> logger) : base(logger)
        {
        }

        [HttpPost]
        public async Task<AccessPointGroup> Create(Guid projectId, AccessPointGroupCreateParams? createParams)
        {
            createParams ??= new AccessPointGroupCreateParams();
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessPointGroupWrite);

            // create a certificate if it is not given
            Certificate certificate;
            if (createParams.CertificateId != null)
            {
                certificate = await vhContext.Certificates.SingleAsync(x => x.ProjectId == projectId && x.CertificateId == createParams.CertificateId);
            }
            else
            {
                await VerifyUserPermission(vhContext, projectId, Permissions.CertificateAdd);
                certificate = CertificateController.CreateInternal(projectId, null);
                vhContext.Certificates.Add(certificate);
            }

            // create default name
            var accessPointGroupName = createParams.AccessPointGroupName?.Trim();
            if (string.IsNullOrEmpty(accessPointGroupName))
            {
                var all = await vhContext.AccessPointGroups.ToArrayAsync();
                for (var i = 1; ; i++)
                {
                    accessPointGroupName = $"AccessPoint Group {i}";
                    if (all.All(x => x.AccessPointGroupName != accessPointGroupName))
                        break;
                }
            }

            var id = Guid.NewGuid();
            var ret = new AccessPointGroup
            {
                ProjectId = projectId,
                AccessPointGroupId = id,
                AccessPointGroupName = accessPointGroupName,
                CertificateId = certificate.CertificateId,
                CreatedTime = DateTime.UtcNow
            };

            await vhContext.AccessPointGroups.AddAsync(ret);
            await vhContext.SaveChangesAsync();
            return ret;
        }

        [HttpPut("{accessPointGroupId}")]
        public async Task Update(Guid projectId, Guid accessPointGroupId, AccessPointGroupUpdateParams updateParams)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessPointGroupWrite);

            var accessPointGroup = await vhContext.AccessPointGroups.SingleAsync(x =>
                x.ProjectId == projectId && x.AccessPointGroupId == accessPointGroupId);

            // check createParams.CertificateId access
            var certificate = updateParams.CertificateId != null
                ? await vhContext.Certificates.SingleAsync(x => x.ProjectId == projectId && x.CertificateId == updateParams.CertificateId)
                : null;

            // change other properties
            if (updateParams.AccessPointGroupName != null) accessPointGroup.AccessPointGroupName = updateParams.AccessPointGroupName.Value;
            if (certificate != null) accessPointGroup.CertificateId = certificate.CertificateId;

            // update
            vhContext.AccessPointGroups.Update(accessPointGroup);
            await vhContext.SaveChangesAsync();
        }

        [HttpGet("{accessPointGroupId}")]
        public async Task<AccessPointGroup> Get(Guid projectId, Guid accessPointGroupId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessPointGroupRead);

            var ret = await vhContext.AccessPointGroups
                .Include(x => x.AccessPoints)
                .SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == accessPointGroupId);

            return ret;
        }

        [HttpGet]
        public async Task<AccessPointGroup[]> List(Guid projectId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessPointGroupRead);

            var ret = await vhContext.AccessPointGroups
                .Include(x => x.AccessPoints)
                .Where(x => x.ProjectId == projectId)
                .ToArrayAsync();

            return ret;
        }


        [HttpDelete("{accessPointGroupId:guid}")]
        public async Task Delete(Guid projectId, Guid accessPointGroupId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessPointGroupWrite);

            var accessPointGroup = await vhContext.AccessPointGroups
                .SingleAsync(e => e.ProjectId == projectId && e.AccessPointGroupId == accessPointGroupId);
            vhContext.AccessPointGroups.Remove(accessPointGroup);
            await vhContext.SaveChangesAsync();
        }
    }
}