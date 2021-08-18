using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;

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
            VhContext vhContext = new();
            return await vhContext.Projects.SingleAsync(e => e.ProjectId == projectId);
        }

        [HttpPost]
        public async Task<Project> Create(Guid? projectId = null)
        {
            VhContext vhContext = new();

            // group
            AccessTokenGroup accessTokenGroup = new()
            {
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
                    new AccessToken()
                    {
                        AccessTokenGroup = accessTokenGroup,
                        AccessTokenName = "Public",
                        SupportCode = 1000,
                        IsPublic = true,
                    },

                    new AccessToken()
                    {
                        AccessTokenGroup = accessTokenGroup,
                        AccessTokenName = "Private 1",
                        IsPublic = false,
                        SupportCode = 1001,
                        MaxClient = 5,
                    }
                }
            };

            await vhContext.Projects.AddAsync(project);
            await vhContext.SaveChangesAsync();
            return project;
        }
    }
}
