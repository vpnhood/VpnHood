using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Security.Cryptography;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("{accountId}/[controller]s")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class AccountController : SuperController<AccountController>
    {
        public AccountController(ILogger<AccountController> logger) : base(logger)
        {
        }

        [HttpGet]
        public async Task<Account> Create()
        {
            VhContext vhContext = new();

            // create account
            Account account = new()
            {
                AccountId = Guid.NewGuid(),
            };

            // group
            AccessTokenGroup accessTokenGroup = new()
            {
                AccessTokenGroupName = "Group1",
                IsDefault = true
            };
            account.AccessTokenGroups.Add(accessTokenGroup);

            // public token
            account.AccessTokens.Add(new AccessToken()
            {
                AccessTokenGroup = accessTokenGroup,
                AccessTokenName = "Public",
                SupportCode = 1000,
                IsPublic = true,
            });
            await vhContext.Accounts.AddAsync(account);

            // private 1
            account.AccessTokens.Add(new AccessToken()
            {
                AccessTokenGroup = accessTokenGroup,
                AccessTokenName = "Private 1",
                IsPublic = false,
                SupportCode = 1001,
                MaxClient = 5,
            });

            await vhContext.Accounts.AddAsync(account);
            await vhContext.SaveChangesAsync();
            return account;
        }
    }
}
