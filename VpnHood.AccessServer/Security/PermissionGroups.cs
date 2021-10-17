using System;
using System.Collections.Generic;
using System.Linq;
using VpnHood.AccessServer.Authorization;
using VpnHood.AccessServer.Authorization.Models;

namespace VpnHood.AccessServer.Security
{
    public static class PermissionGroups
    {
        public static PermissionGroup UserBasic { get; } = new(Guid.Parse("{44F09001-3C94-48D7-A2AC-88CEF10B27E3}"), nameof(UserBasic))
            {
                Permissions = new List<Permission>
                {
                    Permissions.ProjectCreate,
                    Permissions.ProjectList,
                    Permissions.UserRead
                }
            };

        public static PermissionGroup ProjectViewer { get; } = new(Guid.Parse("{043310B2-752F-4087-BC1B-E63B431A45FA}"), nameof(ProjectViewer))
        {
            Permissions = new List<Permission>
            {
                Permissions.AccessTokenRead,
                Permissions.UserRead,
            }
        };

        public static PermissionGroup ProjectOwner { get; } = new(Guid.Parse("{90DD2AC7-7F41-4E07-8977-FBC3C610AEAE}"), nameof(ProjectOwner))
        {
            Permissions = new List<Permission>
            {
                Permissions.ProjectCreate,
                Permissions.ProjectRead,
                Permissions.ProjectWrite,
                Permissions.CertificateRead,
                Permissions.CertificateWrite,
                Permissions.CertificateExport,
                Permissions.AccessTokenRead,
                Permissions.AccessTokenWrite,
                Permissions.AccessTokenReadAccessKey,
                Permissions.UserRead,
                Permissions.UserWrite,
                Permissions.ServerRead,
                Permissions.ServerWrite,
                Permissions.ServerReadConfig,
                Permissions.AccessPointRead,
                Permissions.AccessPointWrite,
                Permissions.AccessPointGroupRead,
                Permissions.AccessPointGroupWrite,
                Permissions.ClientRead
            }
        };

        public static PermissionGroup SystemAdmin { get; } = new(AuthManager.SystemAdminPermissionGroupId, "Administrators")
        {
            Permissions = new List<Permission>(ProjectOwner.Permissions)
                {
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
            ProjectViewer,
            UserBasic
        };

    }
}