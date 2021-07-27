using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;
using Microsoft.EntityFrameworkCore;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class AccountController : SuperController<AccountController>
    {
        public AccountController(ILogger<AccountController> logger) : base(logger)
        {
        }

        [HttpGet]
        [Route(nameof(Create))]
        public async Task<Account> Create()
        {
            VhContext vhContext = new();

            // create account
            Account account = new()
            {
                AccountId = Guid.NewGuid()
            };
            await vhContext.Accounts.AddAsync(account);

            // create default AccessTokenGroup
            ServerEndPointGroup accessTokenGroup = new()
            {
                AccountId = account.AccountId,
                ServerEndPointGroupId = Guid.NewGuid(),
                ServerEndPointGroupName = "Group1",
                IsDefault = true
            };
            await vhContext.ServerEndPointGroups.AddAsync(accessTokenGroup);
            await vhContext.SaveChangesAsync();

            return account;
        }
    }
}
