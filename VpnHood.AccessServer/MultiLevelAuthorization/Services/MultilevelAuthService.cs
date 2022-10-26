using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.MultiLevelAuthorization.Models;
using VpnHood.AccessServer.MultiLevelAuthorization.Persistence;

namespace VpnHood.AccessServer.MultiLevelAuthorization.Services;

public class MultilevelAuthService
{
    private readonly MultilevelAuthContext _authDbContext;

    public static Guid SystemSecureObjectTypeId { get; } = Guid.Parse("{C0139063-35AA-4497-B588-D147673CFA61}");
    public static Guid SystemSecureObjectId { get; } = Guid.Parse("{C0133593-DAFE-4667-BA0F-0823DD40947D}");
    public static Guid SystemPermissionGroupId { get; } = Guid.Parse("{1543EDF1-804C-412F-A676-8791827EFBCC}");
    public static Guid SystemRoleId { get; } = Guid.Parse("{6692AECD-183F-49BC-8B5E-54C5A5B5CBA2}");
    public static Guid SystemUserId { get; } = Guid.Parse("{186C4064-0B9E-4840-BCB0-6FAECE4B3E7E}");
    public static Guid SystemAdminPermissionGroupId { get; } = Guid.Parse("{20A1760B-0AFD-435F-8E47-36FFDF8159F9}");
    public static Guid SystemAdminRoleId { get; } = Guid.Parse("{C26DBD6F-78E5-4401-94C9-BFB70A628728}");
    public static SecureObjectType SystemSecureObjectType { get; set; } = new(SystemSecureObjectTypeId, "System");

    public MultilevelAuthService(MultilevelAuthContext authDbContext)
    {
        _authDbContext = authDbContext;
    }

    public async Task Init(SecureObjectType[] secureObjectTypes, Permission[] permissions, PermissionGroup[] permissionGroups, bool removeOtherPermissionGroups = true)
    {
        // System objects must exists
        if (secureObjectTypes.All(x => x.SecureObjectTypeId != SystemSecureObjectTypeId)) secureObjectTypes = secureObjectTypes.Concat(new[] { SystemSecureObjectType }).ToArray();
        if (permissionGroups.All(x => x.PermissionGroupId != SystemPermissionGroupId)) permissionGroups = permissionGroups.Concat(new[] { new PermissionGroup(SystemPermissionGroupId, "System") }).ToArray();

        await UpdateSecureObjectTypes(secureObjectTypes);
        await UpdatePermissions(permissions);
        await UpdatePermissionGroups(permissionGroups, removeOtherPermissionGroups);

        // init SystemSecureObjectId
        if (await _authDbContext.SecureObjects.AllAsync(x => x.SecureObjectId != SystemSecureObjectId))
        {
            await _authDbContext.SecureObjects.AddAsync(new SecureObject
            {
                SecureObjectId = SystemSecureObjectId,
                SecureObjectTypeId = SystemSecureObjectTypeId,
                ParentSecureObjectId = null
            });
        }

        // init SystemRole
        if (await _authDbContext.Roles.AllAsync(x => x.RoleId != SystemRoleId))
        {
            var role = new Role
            {
                SecureObjectId = SystemSecureObjectId,
                CreatedTime = DateTime.UtcNow,
                ModifiedByUserId = SystemUserId,
                RoleId = SystemRoleId,
                RoleName = "Systems"
            };
            await _authDbContext.Roles.AddAsync(role);

            await Role_AddUser(role.RoleId, SystemUserId, SystemUserId);

            await _authDbContext.SecureObjectRolePermissions.AddAsync(new SecureObjectRolePermission
            {
                SecureObjectId = SystemSecureObjectId,
                RoleId = role.RoleId,
                ModifiedByUserId = SystemUserId,
                CreatedTime = DateTime.UtcNow,
                PermissionGroupId = SystemPermissionGroupId
            });
        }

        // init SystemAdminRole
        if (await _authDbContext.Roles.AllAsync(x => x.RoleId != SystemAdminRoleId))
        {
            var role = new Role
            {
                SecureObjectId = SystemSecureObjectId,
                CreatedTime = DateTime.UtcNow,
                ModifiedByUserId = SystemUserId,
                RoleId = SystemAdminRoleId,
                RoleName = "System Administrators"
            };
            await _authDbContext.Roles.AddAsync(role);

            await _authDbContext.SecureObjectRolePermissions.AddAsync(new SecureObjectRolePermission
            {
                SecureObjectId = SystemSecureObjectId,
                RoleId = role.RoleId,
                ModifiedByUserId = SystemUserId,
                CreatedTime = DateTime.UtcNow,
                PermissionGroupId = SystemAdminPermissionGroupId
            });
        }

        // Table function
        await _authDbContext.Database.ExecuteSqlRawAsync(SecureObject_HierarchySql());

        await _authDbContext.SaveChangesAsync();
    }

    private async Task UpdateSecureObjectTypes(SecureObjectType[] obValues)
    {
        var dbValues = await _authDbContext.SecureObjectTypes.ToListAsync();

        // add
        foreach (var obValue in obValues.Where(x => dbValues.All(c => x.SecureObjectTypeId != c.SecureObjectTypeId)))
            await _authDbContext.SecureObjectTypes.AddAsync(obValue);

        // delete
        foreach (var dbValue in dbValues.Where(x => obValues.All(c => x.SecureObjectTypeId != c.SecureObjectTypeId)))
            _authDbContext.SecureObjectTypes.Remove(dbValue);

        // update
        foreach (var dbValue in dbValues)
        {
            var obValue = obValues.SingleOrDefault(x => x.SecureObjectTypeId == dbValue.SecureObjectTypeId);
            if (obValue == null) continue;
            dbValue.SecureObjectTypeName = obValue.SecureObjectTypeName;
        }
    }

    private async Task UpdatePermissions(Permission[] obValues)
    {
        var dbValues = await _authDbContext.Permissions.ToListAsync();

        // add
        foreach (var obValue in obValues.Where(x => dbValues.All(c => x.PermissionId != c.PermissionId)))
            await _authDbContext.Permissions.AddAsync(obValue);

        // delete
        foreach (var dbValue in dbValues.Where(x => obValues.All(c => x.PermissionId != c.PermissionId)))
            _authDbContext.Permissions.Remove(dbValue);

        // update
        foreach (var dbValue in dbValues)
        {
            var obValue = obValues.SingleOrDefault(x => x.PermissionId == dbValue.PermissionId);
            if (obValue == null) continue;
            dbValue.PermissionName = obValue.PermissionName;
        }
    }

    private static void UpdatePermissionGroupPermissions(ICollection<PermissionGroupPermission> dbValues, PermissionGroupPermission[] obValues)
    {
        // add
        foreach (var obValue in obValues.Where(x => dbValues.All(c => x.PermissionId != c.PermissionId)))
            dbValues.Add(obValue);

        // delete
        foreach (var dbValue in dbValues.Where(x => obValues.All(c => x.PermissionId != c.PermissionId)))
            dbValues.Remove(dbValue);
    }

    private async Task UpdatePermissionGroups(PermissionGroup[] obValues, bool removeOthers)
    {
        var dbValues = await _authDbContext.PermissionGroups.Include(x => x.PermissionGroupPermissions).ToListAsync();

        // add
        foreach (var obValue in obValues.Where(x => dbValues.All(c => x.PermissionGroupId != c.PermissionGroupId)))
        {
            var res = await _authDbContext.PermissionGroups.AddAsync(new PermissionGroup(obValue.PermissionGroupId, obValue.PermissionGroupName));
            UpdatePermissionGroupPermissions(res.Entity.PermissionGroupPermissions,
                obValue.Permissions.Select(x => new PermissionGroupPermission { PermissionGroupId = res.Entity.PermissionGroupId, PermissionId = x.PermissionId }).ToArray());
        }

        // delete
        if (removeOthers)
            foreach (var dbValue in dbValues.Where(x => obValues.All(c => x.PermissionGroupId != c.PermissionGroupId)))
                _authDbContext.PermissionGroups.Remove(dbValue);

        // update
        foreach (var dbValue in dbValues)
        {
            var obValue = obValues.SingleOrDefault(x => x.PermissionGroupId == dbValue.PermissionGroupId);
            if (obValue == null) continue;

            UpdatePermissionGroupPermissions(dbValue.PermissionGroupPermissions,
                obValue.Permissions.Select(x => new PermissionGroupPermission { PermissionGroupId = dbValue.PermissionGroupId, PermissionId = x.PermissionId }).ToArray());

            dbValue.PermissionGroupName = obValue.PermissionGroupName;
        }
    }

    public Task<SecureObject> CreateSecureObject(Guid secureObjectId, SecureObjectType secureObjectType)
    {
        return CreateSecureObject(secureObjectId, secureObjectType, new SecureObject { SecureObjectId = SystemSecureObjectId });
    }

    public async Task<SecureObject> CreateSecureObject(Guid secureObjectId, SecureObjectType secureObjectType, SecureObject parentSecureObject)
    {
        SecureObject secureObject = new()
        {
            SecureObjectId = secureObjectId,
            SecureObjectTypeId = secureObjectType.SecureObjectTypeId,
            ParentSecureObjectId = parentSecureObject.SecureObjectId
        };
        await _authDbContext.SecureObjects.AddAsync(secureObject);
        await _authDbContext.SaveChangesAsync();
        return secureObject;
    }

    public async Task<Role> Role_Create(Guid secureObjectId, string roleName, Guid modifiedByUserId)
    {
        var role = new Role
        {
            SecureObjectId = secureObjectId,
            CreatedTime = DateTime.UtcNow,
            ModifiedByUserId = modifiedByUserId,
            RoleId = Guid.NewGuid(),
            RoleName = roleName
        };
        await _authDbContext.Roles.AddAsync(role);
        await _authDbContext.SaveChangesAsync();
        return role;
    }

    public async Task Role_AddUser(Guid roleId, Guid userId, Guid modifiedByUserId)
    {
        var roleUser = new RoleUser
        {
            RoleId = roleId,
            UserId = userId,
            CreatedTime = DateTime.UtcNow,
            ModifiedByUserId = modifiedByUserId
        };
        await _authDbContext.RoleUsers.AddAsync(roleUser);
        await _authDbContext.SaveChangesAsync();
    }

    public async Task<SecureObjectRolePermission> SecureObject_AddRolePermission(SecureObject secureObject, Role role, PermissionGroup permissionGroup, Guid modifiedByUserId)
    {
        var secureObjectRolePermission = new SecureObjectRolePermission
        {
            SecureObjectId = secureObject.SecureObjectId,
            RoleId = role.RoleId,
            PermissionGroupId = permissionGroup.PermissionGroupId,
            CreatedTime = DateTime.UtcNow,
            ModifiedByUserId = modifiedByUserId,
        };
        var ret = await _authDbContext.SecureObjectRolePermissions.AddAsync(secureObjectRolePermission);
        await _authDbContext.SaveChangesAsync();
        return ret.Entity;
    }

    public async Task<SecureObjectUserPermission> SecureObject_AddUserPermission(SecureObject secureObject, Guid userId, PermissionGroup permissionGroup, Guid modifiedByUserId)
    {
        var secureObjectUserPermission = new SecureObjectUserPermission
        {
            SecureObjectId = secureObject.SecureObjectId,
            UserId = userId,
            PermissionGroupId = permissionGroup.PermissionGroupId,
            CreatedTime = DateTime.UtcNow,
            ModifiedByUserId = modifiedByUserId,
        };
        var ret = await _authDbContext.SecureObjectUserPermissions.AddAsync(secureObjectUserPermission);
        await _authDbContext.SaveChangesAsync();
        return ret.Entity;
    }


    // SqlInjection safe by just id parameter as Guid
    public static string SecureObject_HierarchySql()
    {
        var secureObjects = $"{MultilevelAuthContext.Schema}.{nameof(MultilevelAuthContext.SecureObjects)}";
        var secureObjectId = $"{nameof(SecureObject.SecureObjectId)}";
        var parentSecureObjectId = $"{nameof(SecureObject.ParentSecureObjectId)}";

        var sql = @$"
                    WITH SecureObjectParents
                    AS (SELECT SO.*
                        FROM {secureObjects} AS SO
                        WHERE SO.{secureObjectId} = @id
                        UNION ALL
                        SELECT SO.*
                        FROM {secureObjects} AS SO
                            INNER JOIN SecureObjectParents AS PSO
                                ON SO.{secureObjectId} = PSO.{parentSecureObjectId}
                       )
                    SELECT SOP.*
                    FROM SecureObjectParents AS SOP
                    UNION
                    SELECT * FROM {secureObjects} AS SO
                    WHERE SO.{secureObjectId} = @id
                    ".Replace("                    ", "    ");

        var createSql =
            $"CREATE OR ALTER FUNCTION [{MultilevelAuthContext.Schema}].[{nameof(MultilevelAuthContext.SecureObjectHierarchy)}](@id varchar(40))\r\nRETURNS TABLE\r\nAS\r\nRETURN\r\n({sql})";

        return createSql;
    }

    public async Task<SecureObjectRolePermission[]> SecureObject_GetRolePermissionGroups(Guid secureObjectId)
    {
        var ret = await _authDbContext.SecureObjectRolePermissions
            .Include(x => x.Role)
            .Where(x => x.SecureObjectId == secureObjectId)
            .ToArrayAsync();
        return ret;
    }

    public async Task<SecureObjectUserPermission[]> SecureObject_GetUserPermissionGroups(Guid secureObjectId)
    {
        var ret = await _authDbContext.SecureObjectUserPermissions
            .Where(x => x.SecureObjectId == secureObjectId)
            .ToArrayAsync();
        return ret;
    }

    public async Task<Permission[]> SecureObject_GetUserPermissions(Guid secureObjectId, Guid userId)
    {
        var db = _authDbContext;
        var query1 =
            from secureObject in db.SecureObjectHierarchy(secureObjectId)
            join rolePermission in db.SecureObjectRolePermissions on secureObject.SecureObjectId equals rolePermission.SecureObjectId
            join permissionGroupPermission in db.PermissionGroupPermissions on rolePermission.PermissionGroupId equals permissionGroupPermission.PermissionGroupId
            join role in db.Roles on rolePermission.RoleId equals role.RoleId
            join roleUser in db.RoleUsers on role.RoleId equals roleUser.RoleId
            where roleUser.UserId == userId
            select permissionGroupPermission.Permission;

        var query2 =
            from secureObject in db.SecureObjectHierarchy(secureObjectId)
            join userPermission in db.SecureObjectUserPermissions on secureObject.SecureObjectId equals userPermission.SecureObjectId
            join permissionGroupPermission in db.PermissionGroupPermissions on userPermission.PermissionGroupId equals permissionGroupPermission.PermissionGroupId
            where userPermission.UserId == userId
            select permissionGroupPermission.Permission;


        var ret = await query1
            .Union(query2)
            .Distinct()
            .ToArrayAsync();

        return ret;
    }

    public async Task<bool> SecureObject_HasUserPermission(Guid secureObjectId, Guid userId, Permission permission)
    {
        await using var trans = await _authDbContext.WithNoLockTransaction();
        var permissions = await SecureObject_GetUserPermissions(secureObjectId, userId);
        return permissions.Any(x => x.PermissionId == permission.PermissionId);
    }

    public async Task SecureObject_VerifyUserPermission(Guid secureObjectId, Guid userId,
        Permission permission)
    {
        if (secureObjectId == Guid.Empty)
            throw new SecurityException($"{nameof(secureObjectId)} can not be empty!");

        if (!await SecureObject_HasUserPermission(secureObjectId, userId, permission))
            throw new SecurityException($"You need to grant {permission.PermissionName} permission!");
    }

    public async Task<Role[]> GetUserRolesByPermissionGroup(Guid userId, Guid permissionGroupId)
    {
        await using var trans = await _authDbContext.WithNoLockTransaction();

        var query =
            from role in _authDbContext.Roles
            join secureObjectRolePermission in _authDbContext.SecureObjectRolePermissions on role.RoleId equals secureObjectRolePermission.RoleId
            join userRole in _authDbContext.RoleUsers on role.RoleId equals userRole.RoleId
            where
                secureObjectRolePermission.PermissionGroupId == permissionGroupId &&
                userRole.UserId == userId
            select
                role;

        return await query.Distinct().ToArrayAsync();
    }

    public async Task<Role[]> GetUserRoles(Guid userId)
    {
        await using var trans = await _authDbContext.WithNoLockTransaction();
        var roleUsers = await _authDbContext.RoleUsers
            .Include(x => x.Role)
            .Where(x => x.UserId == userId)
            .Distinct()
            .ToArrayAsync();

        return roleUsers.Select(x => x.Role!).ToArray();
    }
}