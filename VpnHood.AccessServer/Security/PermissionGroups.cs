using System;
using System.Collections.Generic;
using VpnHood.AccessServer.Authorization;
using VpnHood.AccessServer.Authorization.Models;

namespace VpnHood.AccessServer.Security
{
    public static class PermissionGroups
    {
        public static PermissionGroup System { get; } = new(AuthManager.SystemPermissionGroupId, "System")
        {
            Permissions = new List<Permission> { Permissions.CreateProject }
        };

        public static PermissionGroup[] All { get; } = { System };

    }
}