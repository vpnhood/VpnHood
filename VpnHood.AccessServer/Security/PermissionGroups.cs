using System;
using System.Collections.Generic;
using System.Linq;
using VpnHood.AccessServer.Authorization;
using VpnHood.AccessServer.Authorization.Models;

namespace VpnHood.AccessServer.Security
{
    public static class PermissionGroups
    {
        public static PermissionGroup ProjectViewer { get; } = new(Guid.Parse("{043310B2-752F-4087-BC1B-E63B431A45FA}"), "Viewer")
        {
            Permissions = new List<Permission>
            {
                Permissions.ListTokens,
                Permissions.ReadUser,
            }
        };

        public static PermissionGroup ProjectOwner { get; } = new(Guid.Parse("{90DD2AC7-7F41-4E07-8977-FBC3C610AEAE}"), "Admin")
        {
            Permissions = new List<Permission>
            {
                Permissions.CreateProject,
                Permissions.ReadUser,
            }
        };

        public static PermissionGroup SystemAdmin { get; } = new(AuthManager.SystemAdminPermissionGroupId, "Administrators")
        {
            Permissions = new List<Permission>(ProjectOwner.Permissions)
                {
                    Permissions.UpdateUser
                }
                .Distinct()
                .ToArray()
        };

        public static PermissionGroup System { get; } = new(AuthManager.SystemPermissionGroupId, "System")
        {
            Permissions = new List<Permission>(SystemAdmin.Permissions)
                .Distinct()
                .ToArray()
        };

        public static PermissionGroup[] All { get; } =
        {
            System,
            SystemAdmin,
            ProjectOwner,
            ProjectViewer
        };

    }
}