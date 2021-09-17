using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Authorization.Models;

namespace VpnHood.AccessServer.Authorization
{
    public class AuthManager
    {
        private readonly AuthDbContext _dbContext;

        public static Guid SystemSecureObjectTypeId { get; } = Guid.Parse("{C0139063-35AA-4497-B588-D147673CFA61}");
        public static Guid SystemSecureObjectId { get; } = Guid.Parse("{C0133593-DAFE-4667-BA0F-0823DD40947D}");
        public static Guid SystemPermissionGroupId { get; } = Guid.Parse("{1543EDF1-804C-412F-A676-8791827EFBCC}");
        public static Guid SystemRoleId { get; } = Guid.Parse("{6692AECD-183F-49BC-8B5E-54C5A5B5CBA2}");
        public static Guid SystemUserId { get; } = Guid.Parse("{186C4064-0B9E-4840-BCB0-6FAECE4B3E7E}");
        public static SecureObjectType SystemSecureObjectType { get; set; } = new(SystemSecureObjectTypeId, "System");

        public AuthManager(AuthDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        internal async Task Init(SecureObjectType[] secureObjectTypes, Permission[] permissions, PermissionGroup[] permissionGroups, bool removeOtherPermissionGroups = true)
        {
            // System objects must exists
            if (secureObjectTypes.All(x => x.SecureObjectTypeId != SystemSecureObjectTypeId)) secureObjectTypes = secureObjectTypes.Concat(new[] { SystemSecureObjectType }).ToArray();
            if (permissionGroups.All(x => x.PermissionGroupId != SystemPermissionGroupId)) permissionGroups = permissionGroups.Concat(new[] { new PermissionGroup(SystemPermissionGroupId, "System") }).ToArray();

            await UpdateSecureObjectTypes(secureObjectTypes);
            await UpdatePermissions(permissions);
            await UpdatePermissionGroups(permissionGroups, removeOtherPermissionGroups);

            // init SystemSecureObjectId
            if (await _dbContext.SecureObjects.AllAsync(x => x.SecureObjectId != SystemSecureObjectId))
            {
                await _dbContext.SecureObjects.AddAsync(new SecureObject
                {
                    SecureObjectId = SystemSecureObjectId,
                    SecureObjectTypeId = SystemSecureObjectTypeId,
                    ParentSecureObjectId = null
                });
            }

            // init SystemRole
            if (await _dbContext.Roles.AllAsync(x => x.RoleId != SystemRoleId))
            {
                var role = new Role
                {
                    CreatedTime = DateTime.UtcNow,
                    ModifiedByUserId = SystemUserId,
                    RoleId = SystemRoleId,
                    RoleName = "System"
                };
                await _dbContext.Roles.AddAsync(role);

                await Role_AddUser(role, SystemUserId, SystemUserId);

                await _dbContext.SecureObjectRolePermissions.AddAsync(new SecureObjectRolePermission
                {
                    SecureObjectId = SystemSecureObjectId,
                    RoleId = role.RoleId,
                    ModifiedByUserId = SystemUserId,
                    CreatedTime = DateTime.UtcNow,
                    PermissionGroupId = SystemPermissionGroupId
                });
            }

            // Table function
            await _dbContext.Database.ExecuteSqlRawAsync(SecureObject_HierarchySql());

            await _dbContext.SaveChangesAsync();
        }

        private async Task UpdateSecureObjectTypes(SecureObjectType[] obValues)
        {
            var dbValues = await _dbContext.SecureObjectTypes.ToListAsync();

            // add
            foreach (var obValue in obValues.Where(x => dbValues.All(c => x.SecureObjectTypeId != c.SecureObjectTypeId)))
                await _dbContext.SecureObjectTypes.AddAsync(obValue);

            // delete
            foreach (var dbValue in dbValues.Where(x => obValues.All(c => x.SecureObjectTypeId != c.SecureObjectTypeId)))
                _dbContext.SecureObjectTypes.Remove(dbValue);

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
            var dbValues = await _dbContext.Permissions.ToListAsync();

            // add
            foreach (var obValue in obValues.Where(x => dbValues.All(c => x.PermissionId != c.PermissionId)))
                await _dbContext.Permissions.AddAsync(obValue);

            // delete
            foreach (var dbValue in dbValues.Where(x => obValues.All(c => x.PermissionId != c.PermissionId)))
                _dbContext.Permissions.Remove(dbValue);

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
            var dbValues = await _dbContext.PermissionGroups.Include(x => x.PermissionGroupPermissions).ToListAsync();

            // add
            foreach (var obValue in obValues.Where(x => dbValues.All(c => x.PermissionGroupId != c.PermissionGroupId)))
                await _dbContext.PermissionGroups.AddAsync(obValue);

            // delete
            if (removeOthers)
                foreach (var dbValue in dbValues.Where(x => obValues.All(c => x.PermissionGroupId != c.PermissionGroupId)))
                    _dbContext.PermissionGroups.Remove(dbValue);

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
            await _dbContext.SecureObjects.AddAsync(secureObject);
            return secureObject;
        }

        public async Task<Role> Role_Create(string roleName, Guid modifiedByUserId)
        {
            var role = new Role
            {
                CreatedTime = DateTime.UtcNow,
                ModifiedByUserId = modifiedByUserId,
                RoleId = Guid.NewGuid(),
                RoleName = roleName
            };
            await _dbContext.Roles.AddAsync(role);
            return role;
        }

        private async Task Role_AddUser(Role role, Guid userId, Guid modifiedByUserId)
        {
            var roleUser = new RoleUser
            {
                RoleId = role.RoleId,
                UserId = userId,
                CreatedTime = DateTime.UtcNow,
                ModifiedByUserId = modifiedByUserId
            };
            await _dbContext.RoleUsers.AddAsync(roleUser);
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
            var ret = await _dbContext.SecureObjectRolePermissions.AddAsync(secureObjectRolePermission);
            return ret.Entity;
        }

        // SqlInjection safe by just id parameter as Guid
        public static string SecureObject_HierarchySql()
        {
            var secureObjects = $"{AuthDbContext.Schema}.{nameof(AuthDbContext.SecureObjects)}";
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
                $"CREATE OR ALTER FUNCTION [{AuthDbContext.Schema}].[{nameof(AuthDbContext.SecureObjectHierarchy)}](@id varchar(40))\r\nRETURNS TABLE\r\nAS\r\nRETURN\r\n({sql})";

            return createSql;
        }

        public async Task<Permission[]> SecureObject_GetUserPermissions(SecureObject secureObject, Guid userId)
        {
            var db = _dbContext;
            var query1 =
                from so in db.SecureObjectHierarchy(secureObject.SecureObjectId)
                join rolePermission in db.SecureObjectRolePermissions on so.SecureObjectId equals rolePermission.SecureObjectId
                join permissionGroupPermission in db.PermissionGroupPermissions on rolePermission.PermissionGroupId equals permissionGroupPermission.PermissionGroupId
                join role in db.Roles on rolePermission.RoleId equals role.RoleId
                join roleUser in db.RoleUsers on role.RoleId equals roleUser.RoleId
                where roleUser.UserId == userId
                select permissionGroupPermission.Permission;

            var query2 =
                from so in db.SecureObjectHierarchy(secureObject.SecureObjectId)
                join userPermission in db.SecureObjectUserPermissions on so.SecureObjectId equals userPermission.SecureObjectId
                join permissionGroupPermission in db.PermissionGroupPermissions on userPermission.PermissionGroupId equals permissionGroupPermission.PermissionGroupId
                where userPermission.UserId == userId
                select permissionGroupPermission.Permission;


            var ret = await query1
                .Union(query2)
                .Distinct()
                .ToArrayAsync();

            return ret!;
        }
    }
}