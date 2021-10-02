using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Security;
using VpnHood.Common;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects")]
    public class ProjectController : SuperController<ProjectController>
    {
        public ProjectController(ILogger<ProjectController> logger) : base(logger)
        {
        }

        [HttpGet("{projectId:guid}")]
        public async Task<Project> Get(Guid projectId)
        {
            await using VhContext vhContext = new();
            var query = vhContext.Projects
                .Include(x => x.ProjectRoles);

            var res = await query
                .SingleAsync(e => e.ProjectId == projectId);
            return res;
        }

        [HttpPost]
        public async Task<Project> Create(Guid? projectId = null)
        {
            projectId ??= Guid.NewGuid();
            await using VhContext vhContext = new();
            var curUserId = await GetCurrentUserId(vhContext);

            // Roles
            var adminsRole = await vhContext.AuthManager.Role_Create(Resource.Administrators, curUserId);
            var guestsRole = await vhContext.AuthManager.Role_Create(Resource.Guests, curUserId);

            // Groups
            AccessPointGroup accessPointGroup = new()
            {
                AccessPointGroupId = Guid.NewGuid(),
                AccessPointGroupName = "Group1",
                Certificate = CertificateController.CreateInternal(projectId.Value, null),
                IsDefault = true
            };

            // create project
            Project project = new()
            {
                ProjectId = projectId.Value,
                AccessPointGroups = new HashSet<AccessPointGroup>
                {
                    accessPointGroup,
                },
                AccessTokens = new HashSet<AccessToken>
                {
                    new()
                    {
                        AccessTokenId = Guid.NewGuid(),
                        AccessPointGroup = accessPointGroup,
                        AccessTokenName = "Public",
                        SupportCode = 1000,
                        Secret = Util.GenerateSessionKey(),
                        IsPublic = true
                    },

                    new()
                    {
                        AccessTokenId = Guid.NewGuid(),
                        AccessPointGroup = accessPointGroup,
                        AccessTokenName = "Private 1",
                        IsPublic = false,
                        SupportCode = 1001,
                        MaxClient = 5,
                        Secret = Util.GenerateSessionKey()
                    }
                },
                ProjectRoles = new HashSet<ProjectRole>
                {
                    new()
                    {
                        RoleId = adminsRole.RoleId
                    },
                    new()
                    {
                        RoleId = guestsRole.RoleId
                    }
                }
            };

            await vhContext.Projects.AddAsync(project);

            // Grant permissions
            var secureObject = await vhContext.AuthManager.CreateSecureObject(projectId.Value, SecureObjectTypes.Project);
            await vhContext.AuthManager.SecureObject_AddRolePermission(secureObject, adminsRole, PermissionGroups.Admin, curUserId);
            await vhContext.AuthManager.SecureObject_AddRolePermission(secureObject, guestsRole, PermissionGroups.Guest, curUserId);

            // add current user as the admin
            await vhContext.AuthManager.Role_AddUser(adminsRole, curUserId, curUserId);

            await vhContext.SaveChangesAsync();
            return project;
        }

        [HttpGet]
        public async Task<Project[]> List()
        {
            await using VhContext vhContext = new();
            var curUserId = await GetCurrentUserId(vhContext);

            var query =
                from projectRole in vhContext.ProjectRoles
                join roleUser in vhContext.RoleUsers on projectRole.RoleId equals roleUser.RoleId
                where roleUser.UserId == curUserId
                select projectRole.Project;

            var ret = await query
                .Distinct()
                .ToArrayAsync();

            return ret;
        }
    }
}