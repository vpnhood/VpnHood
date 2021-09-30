using System;
using System.Collections.Generic;
using System.Linq;
using VpnHood.AccessServer.Authorization;
using VpnHood.AccessServer.Authorization.Models;

namespace VpnHood.AccessServer.Security
{
    public static class PermissionGroups
    {
        public static PermissionGroup Guest { get; } = new(Guid.Parse("{043310B2-752F-4087-BC1B-E63B431A45FA}"), "Guest")
        {
            Permissions = new List<Permission>
            {
                Permissions.ListTokens
            }
        };

        public static PermissionGroup Admin { get; } = new(Guid.Parse("{90DD2AC7-7F41-4E07-8977-FBC3C610AEAE}"), "Admin")
        {
            Permissions = new List<Permission>
            {
                Permissions.CreateProject
            }
        };

        public static PermissionGroup System { get; } = new(AuthManager.SystemPermissionGroupId, "System")
        {
            Permissions = new List<Permission>(Admin.Permissions)
                {
                    Permissions.Test
                }
                .Distinct().ToArray()
        };

        public static PermissionGroup[] All { get; } =
        {
            System,
            Admin,
            Guest
        };

    }
}