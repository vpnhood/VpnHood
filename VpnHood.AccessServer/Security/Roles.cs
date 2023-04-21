﻿using System;
using System.Linq;
using GrayMint.Authorization.RoleManagement.SimpleRoleProviders.Dtos;
using GrayMint.Authorization.RoleManagement.TeamControllers.Security;

namespace VpnHood.AccessServer.Security;

public static class Roles
{
    public static SimpleRole ProjectReader { get; } = new()
    {
        RoleName = nameof(ProjectReader),
        RoleId = Guid.Parse("{D7808522-5207-45A2-9924-BAA396398100}"),
        IsRoot = false,
        Permissions = new[]
        {
            nameof(Permissions.ProjectRead)
        }
    };

    public static SimpleRole ProjectWriter { get; } = new()
    {
        RoleName = nameof(ProjectWriter),
        RoleId = Guid.Parse("{1EB1F99D-1419-4C29-8303-FA5A507D8AE2}"),
        IsRoot = false,
        Permissions = new[]
        {
            Permissions.ProjectWrite,
            Permissions.CertificateRead,
            Permissions.CertificateWrite,
            Permissions.CertificateExport,
            Permissions.AccessTokenWrite,
            Permissions.AccessTokenReadAccessKey,
            Permissions.ServerWrite,
            Permissions.ServerInstall,
            Permissions.ServerFarmWrite,
            Permissions.IpLockWrite,
        }.Concat(ProjectReader.Permissions).ToArray()
    };

    public static SimpleRole ProjectAdmin { get; } = new()
    {
        RoleName = nameof(ProjectAdmin),
        RoleId = Guid.Parse("{B16607B0-0580-4AE1-A295-85304FDD0F82}"),
        IsRoot = false,
        Permissions = new[]
        {
            RolePermissions.RoleRead,
            RolePermissions.RoleWrite,
        }.Concat(ProjectWriter.Permissions).ToArray()

    };

    public static SimpleRole ProjectOwner { get; } = new()
    {
        RoleName = nameof(ProjectOwner),
        RoleId = Guid.Parse("{B7E69E64-F722-42F5-95E7-5E8364B9FF58}"),
        IsRoot = false,
        Permissions = new[]
        {
            Permissions.ProjectCreate,
            RolePermissions.RoleWriteOwner,
        }.Concat(ProjectAdmin.Permissions).ToArray()
    };

    public static SimpleRole SystemAdmin { get; } = new()
    {
        RoleName = nameof(SystemAdmin),
        RoleId = Guid.Parse("{A1516270-D4D5-4888-820B-1C558006916F}"),
        IsRoot = true,
        Permissions = new[]
        {
            Permissions.ProjectList,
            Permissions.Sync
        }.Concat(ProjectOwner.Permissions).ToArray()
    };

}