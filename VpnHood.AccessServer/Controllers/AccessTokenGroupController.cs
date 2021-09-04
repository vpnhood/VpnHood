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
    [Route("/api/projects/{projectId:guid}/access-token-groups")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class AccessTokenGroupController : SuperController<AccessTokenGroupController>
    {
        public AccessTokenGroupController(ILogger<AccessTokenGroupController> logger) : base(logger)
        {
        }

        [HttpPost]
        public async Task<AccessTokenGroup> Create(Guid projectId, EndPointGroupCreateParams? createParams)
        {
            createParams ??= new EndPointGroupCreateParams();
            await using VhContext vhContext = new();

            // check createParams.CertificateId access
            if (createParams.CertificateId != null)
                await vhContext.Certificates.SingleAsync(x => x.ProjectId == projectId && x.CertificateId == createParams.CertificateId);

            // remove previous default 
            var prevDefault = vhContext.AccessTokenGroups.FirstOrDefault(x => x.ProjectId == projectId && x.IsDefault);
            if (prevDefault != null && createParams.MakeDefault)
            {
                prevDefault.IsDefault = false;
                vhContext.AccessTokenGroups.Update(prevDefault);
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
            var ret = new AccessTokenGroup
            {
                ProjectId = projectId,
                AccessTokenGroupId = id,
                AccessTokenGroupName = createParams.AccessTokenGroupName?.Trim() ?? id.ToString(),
                CertificateId = certificateId.Value,
                IsDefault = createParams.MakeDefault || prevDefault == null,
                CreatedTime = DateTime.UtcNow
            };

            await vhContext.AccessTokenGroups.AddAsync(ret);
            await vhContext.SaveChangesAsync();
            return ret;
        }

        [HttpPut("{accessTokenGroupId}")]
        public async Task Update(Guid projectId, Guid accessTokenGroupId, EndPointGroupUpdateParams updateParams)
        {
            await using VhContext vhContext = new();
            var accessTokenGroup = await vhContext.AccessTokenGroups.SingleAsync(x =>
                x.ProjectId == projectId && x.AccessTokenGroupId == accessTokenGroupId);

            // check createParams.CertificateId access
            if (updateParams.CertificateId != null)
                await vhContext.Certificates.SingleAsync(x => x.ProjectId == projectId && x.CertificateId == updateParams.CertificateId);

            // transaction required for changing default. EF can not do this due the index
            using var trans = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            // change default
            if (!accessTokenGroup.IsDefault && updateParams.MakeDefault?.Value == true)
            {
                var prevDefault =
                    vhContext.AccessTokenGroups.FirstOrDefault(x => x.ProjectId == projectId && x.IsDefault);
                if (prevDefault != null)
                {
                    prevDefault.IsDefault = false;
                    vhContext.AccessTokenGroups.Update(prevDefault);
                    await vhContext.SaveChangesAsync();
                }

                accessTokenGroup.IsDefault = true;
            }

            // change other properties
            if (updateParams.AccessTokenGroupName != null) accessTokenGroup.AccessTokenGroupName = updateParams.AccessTokenGroupName.Value;
            if (updateParams.CertificateId != null) accessTokenGroup.CertificateId = updateParams.CertificateId.Value;

            // update
            vhContext.AccessTokenGroups.Update(accessTokenGroup);
            await vhContext.SaveChangesAsync();
            trans.Complete();
        }


        [HttpGet("{accessTokenGroupId}")]
        public async Task<AccessTokenGroupData> Get(Guid projectId, Guid accessTokenGroupId)
        {
            await using VhContext vhContext = new();
            var query = from atg in vhContext.AccessTokenGroups
                        join se in vhContext.ServerEndPoints on new { key1 = atg.AccessTokenGroupId, key2 = true } equals new
                        { key1 = se.AccessTokenGroupId, key2 = se.IsDefault } into grouping
                        from e in grouping.DefaultIfEmpty()
                        where atg.ProjectId == projectId && atg.AccessTokenGroupId == accessTokenGroupId
                        select new AccessTokenGroupData { AccessTokenGroup = atg, DefaultServerEndPoint = e };
            return await query.SingleAsync();
        }

        [HttpGet]
        public async Task<AccessTokenGroupData[]> List(Guid projectId)
        {
            await using VhContext vhContext = new();
            var res = await (from eg in vhContext.AccessTokenGroups
                             join e in vhContext.ServerEndPoints on new { key1 = eg.AccessTokenGroupId, key2 = true } equals new
                             { key1 = e.AccessTokenGroupId, key2 = e.IsDefault } into grouping
                             from e in grouping.DefaultIfEmpty()
                             where eg.ProjectId == projectId
                             select new AccessTokenGroupData
                             {
                                 AccessTokenGroup = eg,
                                 DefaultServerEndPoint = e
                             }).ToArrayAsync();

            return res;
        }


        [HttpDelete("{accessTokenGroupId:guid}")]
        public async Task Delete(Guid projectId, Guid accessTokenGroupId)
        {
            await using VhContext vhContext = new();
            var accessTokenGroup = await vhContext.AccessTokenGroups.SingleAsync(e =>
                e.ProjectId == projectId && e.AccessTokenGroupId == accessTokenGroupId);
            if (accessTokenGroup.IsDefault)
                throw new InvalidOperationException("A default group can not be deleted!");
            vhContext.AccessTokenGroups.Remove(accessTokenGroup);
            await vhContext.SaveChangesAsync();
        }
    }
}