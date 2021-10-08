using System;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
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

            // check createParams.CertificateId access
            if (createParams.CertificateId != null)
                await vhContext.Certificates.SingleAsync(x => x.ProjectId == projectId && x.CertificateId == createParams.CertificateId);

            // remove previous default 
            var prevDefault = vhContext.AccessPointGroups.FirstOrDefault(x => x.ProjectId == projectId && x.IsDefault);
            if (prevDefault != null && createParams.MakeDefault)
            {
                prevDefault.IsDefault = false;
                vhContext.AccessPointGroups.Update(prevDefault);
            }

            // create a certificate if it is not specified
            var certificateId = createParams.CertificateId;
            if (certificateId == null)
            {
                var certificate = CertificateController.CreateInternal(projectId, null);
                vhContext.Certificates.Add(certificate);
                certificateId = certificate.CertificateId;
            }

            var id = Guid.NewGuid();
            var ret = new AccessPointGroup
            {
                ProjectId = projectId,
                AccessPointGroupId = id,
                AccessPointGroupName = createParams.AccessPointGroupName?.Trim() ?? id.ToString(),
                CertificateId = certificateId.Value,
                IsDefault = createParams.MakeDefault || prevDefault == null,
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
            var accessPointGroup = await vhContext.AccessPointGroups.SingleAsync(x =>
                x.ProjectId == projectId && x.AccessPointGroupId == accessPointGroupId);

            // check createParams.CertificateId access
            if (updateParams.CertificateId != null)
                await vhContext.Certificates.SingleAsync(x => x.ProjectId == projectId && x.CertificateId == updateParams.CertificateId);

            // transaction required for changing default. EF can not do this due the index
            using var trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            // change default
            if (!accessPointGroup.IsDefault && updateParams.MakeDefault?.Value == true)
            {
                var prevDefault =
                    vhContext.AccessPointGroups.FirstOrDefault(x => x.ProjectId == projectId && x.IsDefault);
                if (prevDefault != null)
                {
                    prevDefault.IsDefault = false;
                    vhContext.AccessPointGroups.Update(prevDefault);
                    await vhContext.SaveChangesAsync();
                }

                accessPointGroup.IsDefault = true;
            }

            // change other properties
            if (updateParams.AccessPointGroupName != null) accessPointGroup.AccessPointGroupName = updateParams.AccessPointGroupName.Value;
            if (updateParams.CertificateId != null) accessPointGroup.CertificateId = updateParams.CertificateId.Value;

            // update
            vhContext.AccessPointGroups.Update(accessPointGroup);
            await vhContext.SaveChangesAsync();
            trans.Complete();
        }


        [HttpGet("{accessPointGroupId}")]
        public async Task<AccessPointGroup> Get(Guid projectId, Guid accessPointGroupId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessPointRead);

            var ret = await vhContext.AccessPointGroups
                .Include(x => x.AccessPoints)
                .SingleAsync(x => x.ProjectId == projectId && x.AccessPointGroupId == accessPointGroupId);

            return ret;
        }

        [HttpGet]
        public async Task<AccessPointGroup[]> List(Guid projectId)
        {
            await using var vhContext = new VhContext();
            await VerifyUserPermission(vhContext, projectId, Permissions.AccessPointRead);

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
            var accessPointGroup = await vhContext.AccessPointGroups
                .SingleAsync(e => e.ProjectId == projectId && e.AccessPointGroupId == accessPointGroupId);
            vhContext.AccessPointGroups.Remove(accessPointGroup);
            await vhContext.SaveChangesAsync();
        }
    }
}