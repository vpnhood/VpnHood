using GrayMint.Common.AspNetCore.SimpleRoleAuthorization;
using System.Linq;

namespace VpnHood.AccessServer.Security;

public static class Roles
{
    public static SimpleRole ProjectReader { get; } = new(
        nameof(ProjectReader),
        new[]
        {
            nameof(Permission.ProjectRead),
        }
    );

    public static SimpleRole ProjectAdmin { get; } = new(
        nameof(ProjectAdmin),
        new[]
        {
            Permission.ProjectCreate,
            Permission.ProjectRead,
            Permission.ProjectWrite,
            Permission.CertificateRead,
            Permission.CertificateWrite,
            Permission.CertificateExport,
            Permission.AccessTokenWrite,
            Permission.AccessTokenReadAccessKey,
            Permission.UserRead,
            Permission.UserWrite,
            Permission.ServerWrite,
            Permission.ServerInstall,
            Permission.AccessPointWrite,
            Permission.ServerFarmWrite,
            Permission.IpLockWrite,
        }
    );

    public static SimpleRole ProjectOwner { get; } = new(
        nameof(ProjectOwner),
        ProjectAdmin.Permissions
    );

    public static SimpleRole SystemAdmin { get; } = new(
        nameof(SystemAdmin),
        new[]
        {
            Permission.Sync
        }.Concat(ProjectOwner.Permissions)
    );

    public static SimpleRole[] All { get; } =
    {
        SystemAdmin,
        ProjectOwner,
        ProjectReader
    };
}