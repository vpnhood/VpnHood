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
    public class ServerEndPointGroupController : SuperController<ServerEndPointGroupController>
    {
        public ServerEndPointGroupController(ILogger<ServerEndPointGroupController> logger) : base(logger)
        {
        }

        [HttpPost]
        [Route(nameof(Create))]
        public async Task<ServerEndPointGroup> Create(string endPointGroupName)
        {
            using VhContext vhContext = new();
            var ret = new ServerEndPointGroup { AccountId = AccountId, ServerEndPointGroupId = Guid.NewGuid(), ServerEndPointGroupName = endPointGroupName };
            await vhContext.ServerEndPointGroups.AddAsync(ret);
            return ret;
        }

        public class GetResult 
        {
            public ServerEndPointGroup ServerEndPointGroup { get; set; }
            public IPEndPoint DefaultEndPoint { get; set; }
        }

        [HttpGet]
        [Route(nameof(Get))]
        public async Task<GetResult> Get(Guid serverEndPointGroupId)
        {
            using VhContext vhContext = new();
            var res = await (from EG in vhContext.ServerEndPointGroups
                             join E in vhContext.ServerEndPoints on EG.ServerEndPointGroupId equals E.ServerEndPointGroupId  into grouping
                             from E in grouping.DefaultIfEmpty()
                             where EG.ServerEndPointGroupId == serverEndPointGroupId && E.IsDefault
                             select new { EG, DefaultEndPoint = IPEndPoint.Parse(E.PulicEndPoint) }).ToListAsync();

            return new GetResult
            {
                ServerEndPointGroup = res.Single().EG,
                DefaultEndPoint = res.Single().DefaultEndPoint
            };
        }

        [HttpDelete]
        [Route(nameof(Delete))]
        public async Task Delete(Guid serverEndPointGroupId)
        {
            using VhContext vhContext = new();
            var endPointGroup = await vhContext.ServerEndPointGroups.SingleAsync(e => e.ServerEndPointGroupId == serverEndPointGroupId);
            vhContext.ServerEndPointGroups.Remove(endPointGroup);
        }
    }
}
