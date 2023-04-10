using GrayMint.Common.AspNetCore.SimpleRoleAuthorization;
using GrayMint.Common.AspNetCore.SimpleUserControllers.Security;
using System;
using System.Linq;
using System.Reflection;

namespace VpnHood.AccessServer.Security;

public static class Roles
{
    public static SimpleRole ProjectReader { get; } = new()
    {
        RoleName = nameof(ProjectReader),
        RoleId = Guid.Parse("{D7808522-5207-45A2-9924-BAA396398100}"),
        IsSystem = false,
        Permissions = new[]
        {
            nameof(Permissions.ProjectRead)
        }
    };

    public static SimpleRole ProjectAdmin { get; } = new()
    {
        RoleName = nameof(ProjectAdmin),
        RoleId = Guid.Parse("{B16607B0-0580-4AE1-A295-85304FDD0F82}"),
        IsSystem = false,
        Permissions = new[]
        {
            Permissions.ProjectCreate,
            Permissions.ProjectRead,
            Permissions.ProjectWrite,
            Permissions.CertificateRead,
            Permissions.CertificateWrite,
            Permissions.CertificateExport,
            Permissions.AccessTokenWrite,
            Permissions.AccessTokenReadAccessKey,
            Permissions.UserRead,
            Permissions.UserWrite,
            Permissions.ServerWrite,
            Permissions.ServerInstall,
            Permissions.ServerFarmWrite,
            Permissions.IpLockWrite,
            RolePermissions.RoleRead,
            RolePermissions.RoleWrite,
        }
    };

    public static SimpleRole ProjectOwner { get; } = new()
    {
        RoleName = nameof(ProjectOwner),
        RoleId = Guid.Parse("{B7E69E64-F722-42F5-95E7-5E8364B9FF58}"),
        IsSystem = false,
        Permissions = new[]
        {
            RolePermissions.RoleWriteOwner,
        }.Concat(ProjectAdmin.Permissions).ToArray()
    };

    public static SimpleRole SystemAdmin { get; } = new()
    {
        RoleName = nameof(SystemAdmin),
        RoleId = Guid.Parse("{A1516270-D4D5-4888-820B-1C558006916F}"),
        IsSystem = true,
        Permissions = new[]
        {
            Permissions.ProjectList,
            Permissions.Sync
        }.Concat(ProjectOwner.Permissions).ToArray()
    };

    public static SimpleRole[] All
    {
        get
        {
            var properties = typeof(Roles)
                .GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(x => x.PropertyType == typeof(SimpleRole));

            var roles = properties.Select(propertyInfo => (SimpleRole)propertyInfo.GetValue(null)!);
            return roles.ToArray();
        }
    }
}