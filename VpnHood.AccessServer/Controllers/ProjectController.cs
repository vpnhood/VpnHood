using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Exceptions;
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
            await using var vhContext = new VhContext();
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
            await using var vhContext = new VhContext();
            var curUserId = await GetCurrentUserId(vhContext);

            // Check user quota
            using var autoWait = new AutoWait($"CreateProject_{curUserId}");

            // get user's maxProjects quota
            var user = await vhContext.Users.SingleAsync(x => x.UserId == curUserId);

            // find all user's project with owner role
            var query =
                from projectRole in vhContext.ProjectRoles
                join secureObjectRolePermission in vhContext.SecureObjectRolePermissions on projectRole.RoleId equals secureObjectRolePermission.RoleId
                join userRole in vhContext.RoleUsers on projectRole.RoleId equals userRole.RoleId
                where
                    secureObjectRolePermission.PermissionGroupId == PermissionGroups.ProjectOwner.PermissionGroupId &&
                    userRole.UserId == user.UserId
                select projectRole.ProjectId;

            var userProjectOwnerCount = await query.Distinct().CountAsync();
            if (userProjectOwnerCount >= user.MaxProjectCount)
            {
                throw new QuotaException($"You cannot own more than {user.MaxProjectCount} projects!",
                    nameof(user.MaxProjectCount), user.MaxProjectCount.ToString());
            }

            // Roles
            var ownerRole = await vhContext.AuthManager.Role_Create(Resource.ProjectOwners, curUserId);
            var guestsRole = await vhContext.AuthManager.Role_Create(Resource.ProjectGuests, curUserId);

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
                        RoleId = ownerRole.RoleId
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
            await vhContext.AuthManager.SecureObject_AddRolePermission(secureObject, ownerRole, PermissionGroups.ProjectOwner, curUserId);
            await vhContext.AuthManager.SecureObject_AddRolePermission(secureObject, guestsRole, PermissionGroups.ProjectViewer, curUserId);

            // add current user as the admin
            await vhContext.AuthManager.Role_AddUser(ownerRole.RoleId, curUserId, curUserId);

            await vhContext.SaveChangesAsync();
            return project;
        }

        [HttpGet]
        public async Task<Project[]> List()
        {
            await using var vhContext = new VhContext();
            var curUserId = await GetCurrentUserId(vhContext);

            var query =
                from project in vhContext.Projects
                join projectRole in vhContext.ProjectRoles on project.ProjectId equals projectRole.ProjectId
                join roleUser in vhContext.RoleUsers on projectRole.RoleId equals roleUser.RoleId
                where roleUser.UserId == curUserId
                select project;

            var ret = await query
                .Distinct()
                .ToArrayAsync();

            return ret ?? Array.Empty<Project>();
        }
    }
}