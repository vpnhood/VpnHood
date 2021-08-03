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
    [Route("{projectId}/[controller]s")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class AccountController : SuperController<AccountController>
    {
        public AccountController(ILogger<AccountController> logger) : base(logger)
        {
        }

        [HttpGet]
        public async Task<Project> Create()
        {
            VhContext vhContext = new();

            // create project
            Project project = new()
            {
                ProjectId = Guid.NewGuid(),
            };

            // group
            AccessTokenGroup accessTokenGroup = new()
            {
                AccessTokenGroupName = "Group1",
                IsDefault = true
            };
            project.AccessTokenGroups.Add(accessTokenGroup);

            // public token
            project.AccessTokens.Add(new AccessToken()
            {
                AccessTokenGroup = accessTokenGroup,
                AccessTokenName = "Public",
                SupportCode = 1000,
                IsPublic = true,
            });
            await vhContext.Projects.AddAsync(project);

            // private 1
            project.AccessTokens.Add(new AccessToken()
            {
                AccessTokenGroup = accessTokenGroup,
                AccessTokenName = "Private 1",
                IsPublic = false,
                SupportCode = 1001,
                MaxClient = 5,
            });

            await vhContext.Projects.AddAsync(project);
            await vhContext.SaveChangesAsync();
            return project;
        }
    }
}
