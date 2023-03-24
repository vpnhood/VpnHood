using GrayMint.Common.AspNetCore.SimpleRoleAuthorization;
using System;
using System.Linq;

namespace VpnHood.AccessServer.Security;

public static class Roles
{
    public static SimpleRole ProjectReader { get; } = new(
        nameof(ProjectReader),
        Guid.Parse("{D7808522-5207-45A2-9924-BAA396398100}"),
        new[]
        {
            nameof(Permissions.ProjectRead),
        }
    );

    public static SimpleRole ProjectAdmin { get; } = new(
        nameof(ProjectAdmin),
        Guid.Parse("{B16607B0-0580-4AE1-A295-85304FDD0F82}"), 
        new[]
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
            Permissions.AccessPointWrite,
            Permissions.ServerFarmWrite,
            Permissions.IpLockWrite,
        }
    );

    public static SimpleRole ProjectOwner { get; } = new(
        nameof(ProjectOwner),
        Guid.Parse("{B7E69E64-F722-42F5-95E7-5E8364B9FF58}"),
        ProjectAdmin.Permissions
    );

    public static SimpleRole SystemAdmin { get; } = new(
        nameof(SystemAdmin),
        Guid.Parse("{A1516270-D4D5-4888-820B-1C558006916F}"),
        new[]
        {
            Permissions.ProjectList,
            Permissions.Sync
        }.Concat(ProjectOwner.Permissions)
    );

    public static SimpleRole[] All { get; } =
    {
        SystemAdmin,
        ProjectOwner,
        ProjectAdmin,
        ProjectReader
    };
}