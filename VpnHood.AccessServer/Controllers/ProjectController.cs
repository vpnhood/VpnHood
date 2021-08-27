using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;
using VpnHood.Common;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId}")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
    public class ProjectController : SuperController<ProjectController>
    {
        public ProjectController(ILogger<ProjectController> logger) : base(logger)
        {
        }

        [HttpGet]
        public async Task<Project> Get(Guid projectId)
        {
            await using VhContext vhContext = new();
            return await vhContext.Projects.SingleAsync(e => e.ProjectId == projectId);
        }

        [HttpPost]
        public async Task<Project> Create(Guid? projectId = null)
        {
            await using VhContext vhContext = new();

            // group
            AccessTokenGroup accessTokenGroup = new()
            {
                AccessTokenGroupId = Guid.NewGuid(),
                AccessTokenGroupName = "Group1",
                IsDefault = true
            };


            // create project
            Project project = new()
            {
                ProjectId = projectId ?? Guid.NewGuid(),
                AccessTokenGroups = new HashSet<AccessTokenGroup>
                {
                    accessTokenGroup
                },
                AccessTokens = new HashSet<AccessToken>
                {
                    new()
                    {
                        AccessTokenId = Guid.NewGuid(),
                        AccessTokenGroup = accessTokenGroup,
                        AccessTokenName = "Public",
                        SupportCode = 1000,
                        Secret = Util.GenerateSessionKey(),
                        IsPublic = true
                    },

                    new()
                    {
                        AccessTokenId = Guid.NewGuid(),
                        AccessTokenGroup = accessTokenGroup,
                        AccessTokenName = "Private 1",
                        IsPublic = false,
                        SupportCode = 1001,
                        MaxClient = 5,
                        Secret = Util.GenerateSessionKey()
                    }
                }
            };

            await vhContext.Projects.AddAsync(project);
            await vhContext.SaveChangesAsync();
            return project;
        }
    }
}