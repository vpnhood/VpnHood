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
        public static Guid SystemPermissionGroupId { get; } = Guid.Parse("{1543EDF1-804C-412F-A676-8791827EFBCC}");
        public static Guid SystemObjectId { get; } = Guid.Parse("{186C4064-0B9E-4840-BCB0-6FAECE4B3E7E}");
        public static Guid SystemSecureObjectId { get; } = Guid.Parse("{C0133593-DAFE-4667-BA0F-0823DD40947D}");
        public static Guid SystemRoleId { get; } = Guid.Parse("{6692AECD-183F-49BC-8B5E-54C5A5B5CBA2}");
        public static Guid SystemUserId { get; } = Guid.Parse("{186C4064-0B9E-4840-BCB0-6FAECE4B3E7E}");
        public static SecureObjectType SystemSecureObjectType { get; set; } = new(SystemSecureObjectTypeId, "System");

        public AuthManager(AuthDbContext dbContext)
        {
            _dbContext = dbContext;
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

        internal async Task Init(SecureObjectType[] secureObjectTypes, Permission[] permissions, PermissionGroup[] permissionGroups, bool removeOtherPermissionGroups = true)
        {
            // System objects must exists
            if (secureObjectTypes.All(x => x.SecureObjectTypeId != SystemSecureObjectTypeId)) secureObjectTypes = secureObjectTypes.Concat(new[] { SystemSecureObjectType }).ToArray();
            if (permissionGroups.All(x => x.PermissionGroupId != SystemPermissionGroupId)) permissionGroups = permissionGroups.Concat(new[] { new PermissionGroup(SystemPermissionGroupId, "System") }).ToArray();

            await UpdateSecureObjectTypes(secureObjectTypes);
            await UpdatePermissions(permissions);
            await UpdatePermissionGroups(permissionGroups, removeOtherPermissionGroups);
            await _dbContext.SaveChangesAsync();
            return;

            //// init SystemSecureObjectId
            //if (await _dbContext.SecureObjects.AllAsync(x => x.SecureObjectId != AuthSystemId.SecureObjectId))
            //{
            //    if (await _dbContext.SecureObjectTypes.AllAsync(x => x.SecureObjectTypeId != AuthSystemId.SecureObjectTypeId))
            //        await _dbContext.SecureObjectTypes.AddAsync(new SecureObjectType(AuthSystemId.SecureObjectTypeId, "System"));

            //    await _dbContext.SecureObjects.AddAsync(new SecureObject
            //    {
            //        SecureObjectId = AuthSystemId.SecureObjectId,
            //        ObjectId = AuthSystemId.ObjectId,
            //        SecureObjectTypeId = AuthSystemId.SecureObjectTypeId,
            //        ParentSecureObjectId = null
            //    });
            //}

            ////init SystemPermissionGroupId
            //if (await _dbContext.PermissionGroups.AllAsync(x => x.PermissionGroupId != AuthSystemId.PermissionGroupId))
            //    await _dbContext.PermissionGroups.AddAsync(new PermissionGroup(AuthSystemId.PermissionGroupId, "System"));

            //// init SystemRole
            //if (await _dbContext.Roles.AllAsync(x => x.RoleId != AuthSystemId.RoleId))
            //{
            //    var role = new Role
            //    {
            //        CreatedTime = DateTime.UtcNow,
            //        ModifiedByUserId = AuthSystemId.UserId,
            //        RoleId = Guid.NewGuid(),
            //        RoleName = "System"
            //    };
            //    await AddRoleToSecureObject(AuthSystemId.SecureObjectId, role.RoleId, AuthSystemId.PermissionGroupId, AuthSystemId.UserId);
            //}

            await _dbContext.SaveChangesAsync();
        }

        public async Task AddUserToObject(Guid secureObjectId, Guid userId, Guid permissionGroupId, Guid modifiedByUserId)
        {
            var secureObject = await _dbContext.SecureObjects.SingleAsync(x => x.SecureObjectId == secureObjectId);
            await AddUserToSecureObject(secureObject.SecureObjectId, userId, permissionGroupId, modifiedByUserId);
        }

        public async Task AddRoleToObject(Guid secureObjectId, Guid roleId, Guid permissionGroupId, Guid modifiedByUserId)
        {
            var secureObject = await _dbContext.SecureObjects.SingleAsync(x => x.SecureObjectId == secureObjectId);
            await AddRoleToSecureObject(secureObject.SecureObjectId, roleId, permissionGroupId, modifiedByUserId);
        }

        public async Task AddUserToSecureObject(Guid secureObjectId, Guid userId, Guid permissionGroupId, Guid modifiedByUserId)
        {
            await _dbContext.SecureObjectUserPermissions.AddAsync(new SecureObjectUserPermission
            {
                SecureObjectId = secureObjectId,
                CreatedTime = DateTime.UtcNow,
                PermissionGroupId = permissionGroupId,
                UsedId = userId,
                ModifiedByUserId = modifiedByUserId
            });
        }

        public async Task AddRoleToSecureObject(Guid secureObjectId, Guid roleId, Guid permissionGroupId, Guid modifiedByUserId)
        {
            await _dbContext.SecureObjectRolePermissions.AddAsync(new SecureObjectRolePermission
            {
                SecureObjectId = secureObjectId,
                CreatedTime = DateTime.UtcNow,
                PermissionGroupId = permissionGroupId,
                RoleId = roleId,
                ModifiedByUserId = modifiedByUserId
            });
        }

        public async Task<SecureObject> CreateSecureObject(Guid secureObjectId, Guid secureObjectTypeId, Guid parentSecureObjectId)
        {
            SecureObject secureObject = new()
            {
                SecureObjectId = secureObjectId,
                SecureObjectTypeId = secureObjectTypeId,
                ParentSecureObjectId = parentSecureObjectId
            };
            await _dbContext.SecureObjects.AddAsync(secureObject);
            return secureObject;
        }

        public async Task<Role> CreateRole(string roleName, Guid modifiedByUserId)
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
    }
}