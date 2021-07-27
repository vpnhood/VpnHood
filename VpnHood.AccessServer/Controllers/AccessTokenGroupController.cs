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
        public async Task<AccessTokenGroup> Create(string accessTokenGroupName)
        {
            using VhContext vhContext = new();
            var ret = new AccessTokenGroup { AccountId = AccountId, AccessTokenGroupId = Guid.NewGuid(), AccessTokenGroupName = accessTokenGroupName };
            await vhContext.AccessTokenGroups.AddAsync(ret);
            await vhContext.SaveChangesAsync();
            return ret;
        }

        public class GetResult 
        {
            public AccessTokenGroup AccessTokenGroup { get; set; }
            public IPEndPoint DefaultEndPoint { get; set; }
        }

        [HttpGet]
        [Route(nameof(Get))]
        public async Task<GetResult> Get(Guid accessTokenGroupId)
        {
            using VhContext vhContext = new();
            var res = await (from EG in vhContext.AccessTokenGroups
                             join E in vhContext.ServerEndPoints on EG.AccessTokenGroupId equals E.AccessTokenGroupId  into grouping
                             from E in grouping.DefaultIfEmpty()
                             where EG.AccessTokenGroupId == accessTokenGroupId && E.IsDefault
                             select new { EG, DefaultEndPoint = IPEndPoint.Parse(E.PulicEndPoint) }).ToListAsync();

            return new GetResult
            {
                AccessTokenGroup = res.Single().EG,
                DefaultEndPoint = res.Single().DefaultEndPoint
            };
        }

        [HttpDelete]
        [Route(nameof(Delete))]
        public async Task Delete(Guid accessTokenGroupId)
        {
            using VhContext vhContext = new();
            var accessTokenGroup = await vhContext.AccessTokenGroups.SingleAsync(e => e.AccessTokenGroupId == accessTokenGroupId);
            vhContext.AccessTokenGroups.Remove(accessTokenGroup);
        }
    }
}
