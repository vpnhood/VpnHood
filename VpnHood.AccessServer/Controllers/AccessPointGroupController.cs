using System;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId:guid}/access-point-groups")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class AccessPointGroupController : SuperController<AccessPointGroupController>
    {
        public AccessPointGroupController(ILogger<AccessPointGroupController> logger) : base(logger)
        {
        }

        [HttpPost]
        public async Task<AccessPointGroup> Create(Guid projectId, AccessPointGroupCreateParams? createParams)
        {
            createParams ??= new AccessPointGroupCreateParams();
            await using VhContext vhContext = new();

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
            await using VhContext vhContext = new();
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
        public async Task<AccessPointGroupData> Get(Guid projectId, Guid accessPointGroupId)
        {
            await using VhContext vhContext = new();
            var query = from atg in vhContext.AccessPointGroups
                        join se in vhContext.AccessPoints on new { key1 = atg.AccessPointGroupId, key2 = true } equals new
                        { key1 = se.AccessPointGroupId, key2 = se.IsDefault } into grouping
                        from e in grouping.DefaultIfEmpty()
                        where atg.ProjectId == projectId && atg.AccessPointGroupId == accessPointGroupId
                        select new AccessPointGroupData { AccessPointGroup = atg, DefaultAccessPoint = e };
            return await query.SingleAsync();
        }

        [HttpGet]
        public async Task<AccessPointGroupData[]> List(Guid projectId)
        {
            await using VhContext vhContext = new();
            var res = await (from eg in vhContext.AccessPointGroups
                             join e in vhContext.AccessPoints on new { key1 = eg.AccessPointGroupId, key2 = true } equals new
                             { key1 = e.AccessPointGroupId, key2 = e.IsDefault } into grouping
                             from e in grouping.DefaultIfEmpty()
                             where eg.ProjectId == projectId
                             select new AccessPointGroupData
                             {
                                 AccessPointGroup = eg,
                                 DefaultAccessPoint = e
                             }).ToArrayAsync();

            return res;
        }


        [HttpDelete("{accessPointGroupId:guid}")]
        public async Task Delete(Guid projectId, Guid accessPointGroupId)
        {
            await using VhContext vhContext = new();
            var accessPointGroup = await vhContext.AccessPointGroups.SingleAsync(e =>
                e.ProjectId == projectId && e.AccessPointGroupId == accessPointGroupId);
            if (accessPointGroup.IsDefault)
                throw new InvalidOperationException("A default group can not be deleted!");
            vhContext.AccessPointGroups.Remove(accessPointGroup);
            await vhContext.SaveChangesAsync();
        }
    }
}