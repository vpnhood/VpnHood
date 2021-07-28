using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class ClientController : SuperController<ClientController>
    {
        public ClientController(ILogger<ClientController> logger) : base(logger)
        {
        }

        [HttpGet]
        [Route(nameof(Get))]
        public async Task<Client> Get(Guid accountId, Guid clientId)
        {
            using VhContext vhContext = new();
            var query = from C in vhContext.Clients
                        join AU in vhContext.AccessUsages on C.ClientId equals AU.ClientId
                        join AT in vhContext.AccessTokens on AU.AccessTokenId equals AT.AccessTokenId
                        where AT.AccountId == accountId && C.ClientId == clientId
                        select new { C };

            return (await query.SingleAsync()).C;
        }
    }
}
