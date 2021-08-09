using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;
using Microsoft.EntityFrameworkCore;


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

            // create project
            Project project = new()
            {
                ProjectId = projectId ?? Guid.NewGuid(),
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
