﻿using System;
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
    [Route("{accountId}/[controller]s")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class ClientController : SuperController<ClientController>
    {
        public ClientController(ILogger<ClientController> logger) : base(logger)
        {
        }

        [HttpGet]
        [Route("{clientId}")]
        public async Task<Client> Get(Guid accountId, Guid clientId)
        {
            using VhContext vhContext = new();
            return await vhContext.Clients.SingleAsync(x => x.AccountId == accountId && x.ClientId == clientId);
        }
    }
}
