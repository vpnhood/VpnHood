using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class AccessTokenGroupController : SuperController<AccessTokenGroupController>
    {
        public AccessTokenGroupController(ILogger<AccessTokenGroupController> logger) : base(logger)
        {
        }

        [HttpPost]
        [Route(nameof(Create))]
        public async Task<AccessTokenGroup> Create(Guid accountId, string accessTokenGroupName, bool makeDefault = false)
        {
            using VhContext vhContext = new();

            // remove previous default 
            var prevDefault = vhContext.ServerEndPoints.FirstOrDefault(x => x.AccountId == accountId && x.IsDefault);
            if (prevDefault != null && makeDefault)
            {
                prevDefault.IsDefault = false;
                vhContext.ServerEndPoints.Update(prevDefault);
            }

            var ret = new AccessTokenGroup
            {
                AccountId = accountId,
                AccessTokenGroupId = Guid.NewGuid(),
                AccessTokenGroupName = accessTokenGroupName.Trim(),
                IsDefault = makeDefault || prevDefault == null
            };

            await vhContext.AccessTokenGroups.AddAsync(ret);
            await vhContext.SaveChangesAsync();
            return ret;
        }

        public class GetResult
        {
            public AccessTokenGroup AccessTokenGroup { get; set; }
            public string DefaultEndPoint { get; set; }
        }

        [HttpGet]
        [Route(nameof(Get))]
        public async Task<GetResult> Get(Guid accountId, Guid accessTokenGroupId)
        {
            using VhContext vhContext = new();
            var res = await (from EG in vhContext.AccessTokenGroups
                             join E in vhContext.ServerEndPoints on new { key1 = EG.AccessTokenGroupId, key2 = true } equals new { key1 = E.AccessTokenGroupId, key2 = E.IsDefault } into grouping
                             from E in grouping.DefaultIfEmpty()
                             where EG.AccountId == accountId && EG.AccessTokenGroupId == accessTokenGroupId
                             select new { EG, DefaultEndPoint = E.PulicEndPoint }).ToListAsync();

            return new GetResult
            {
                AccessTokenGroup = res.Single().EG,
                DefaultEndPoint = res.Single().DefaultEndPoint
            };
        }

        [HttpDelete]
        [Route(nameof(Delete))]
        public async Task Delete(Guid accountId, Guid accessTokenGroupId)
        {
            using VhContext vhContext = new();
            var accessTokenGroup = await vhContext.AccessTokenGroups.SingleAsync(e => e.AccountId == accountId && e.AccessTokenGroupId == accessTokenGroupId);
            vhContext.AccessTokenGroups.Remove(accessTokenGroup);
        }

        public async Task Update(Guid accountId, Guid accessTokenGroupId, string accessTokenGroupName = null, bool makeDefault = false)
        {
            using VhContext vhContext = new();
            var accessTokenGroup = await vhContext.AccessTokenGroups.SingleAsync(x => x.AccountId == accountId && x.AccessTokenGroupId == accessTokenGroupId);
            if (!string.IsNullOrEmpty(accessTokenGroupName))
                accessTokenGroup.AccessTokenGroupName = accessTokenGroupName;

            // change default
            if (!accessTokenGroup.IsDefault && makeDefault)
            {
                var prevDefault = vhContext.ServerEndPoints.FirstOrDefault(x => x.AccountId == accountId && x.IsDefault);
                prevDefault.IsDefault = false;
                vhContext.ServerEndPoints.Update(prevDefault);

                accessTokenGroup.IsDefault = true;
            }

            vhContext.AccessTokenGroups.Update(accessTokenGroup);
            await vhContext.SaveChangesAsync();
        }
    }
}
