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

        public static Guid SystemObjectTypeId { get; } = Guid.Parse("{C0139063-35AA-4497-B588-D147673CFA61}");
        public static Guid SystemPermissionGroupId { get; } = Guid.Parse("{1543EDF1-804C-412F-A676-8791827EFBCC}");
        public static Guid SystemObjectId { get; } = Guid.Parse("{186C4064-0B9E-4840-BCB0-6FAECE4B3E7E}");
        public static Guid SystemSecurityDescriptorId { get; } = Guid.Parse("{C0133593-DAFE-4667-BA0F-0823DD40947D}");
        public static Guid SystemRoleId { get; } = Guid.Parse("{6692AECD-183F-49BC-8B5E-54C5A5B5CBA2}");
        public static Guid SystemUserId { get; } = Guid.Parse("{186C4064-0B9E-4840-BCB0-6FAECE4B3E7E}");
        public static ObjectType SystemObjectType { get; set; } = new(SystemObjectTypeId, "System");

        public AuthManager(AuthDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        private async Task UpdateObjectTypes(ObjectType[] obValues)
        {
            var dbValues = await _dbContext.ObjectTypes.ToListAsync();

            // add
            foreach (var obValue in obValues.Where(x => dbValues.All(c => x.ObjectTypeId != c.ObjectTypeId)))
                await _dbContext.ObjectTypes.AddAsync(obValue);

            // delete
            foreach (var dbValue in dbValues.Where(x => obValues.All(c => x.ObjectTypeId != c.ObjectTypeId)))
                _dbContext.ObjectTypes.Remove(dbValue);

            // update
            foreach (var dbValue in dbValues)
            {
                var obValue = obValues.SingleOrDefault(x => x.ObjectTypeId == dbValue.ObjectTypeId);
                if (obValue == null) continue;
                dbValue.ObjectTypeName = obValue.ObjectTypeName;
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

        private void UpdatePermissionGroupPermissions(ICollection<PermissionGroupPermission> dbValues, PermissionGroupPermission[] obValues)
        {
            // add
            foreach (var obValue in obValues.Where(x => dbValues.All(c => x.PermissionId != c.PermissionId)))
                dbValues.Add(obValue);

            // delete
            foreach (var dbValue in dbValues.Where(x => obValues.All(c => x.PermissionId != c.PermissionId)))
                dbValues.Remove(dbValue);
        }

        public async Task UpdatePermissionGroups(PermissionGroup[] obValues, bool removeOthers)
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

        internal async Task Init(ObjectType[] objectTypes, Permission[] permissions, PermissionGroup[] permissionGroups, bool removeOtherPermissionGroups = true)
        {
            // System objects must exists
            if (objectTypes.All(x => x.ObjectTypeId != SystemObjectTypeId)) objectTypes = objectTypes.Concat(new[] { SystemObjectType }).ToArray();
            if (permissionGroups.All(x => x.PermissionGroupId != SystemPermissionGroupId)) permissionGroups = permissionGroups.Concat(new[] { new PermissionGroup(SystemPermissionGroupId, "System") }).ToArray();

            await UpdateObjectTypes(objectTypes);
            await UpdatePermissions(permissions);
            await UpdatePermissionGroups(permissionGroups, removeOtherPermissionGroups);
            await _dbContext.SaveChangesAsync();
            return;

            //// init SystemSecurityDescriptorId
            //if (await _dbContext.SecurityDescriptors.AllAsync(x => x.SecurityDescriptorId != AuthSystemId.SecurityDescriptorId))
            //{
            //    if (await _dbContext.ObjectTypes.AllAsync(x => x.ObjectTypeId != AuthSystemId.ObjectTypeId))
            //        await _dbContext.ObjectTypes.AddAsync(new ObjectType(AuthSystemId.ObjectTypeId, "System"));

            //    await _dbContext.SecurityDescriptors.AddAsync(new SecurityDescriptor
            //    {
            //        SecurityDescriptorId = AuthSystemId.SecurityDescriptorId,
            //        ObjectId = AuthSystemId.ObjectId,
            //        ObjectTypeId = AuthSystemId.ObjectTypeId,
            //        ParentSecurityDescriptorId = null
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
            //    await AddRoleToSecurityDescriptor(AuthSystemId.SecurityDescriptorId, role.RoleId, AuthSystemId.PermissionGroupId, AuthSystemId.UserId);
            //}

            await _dbContext.SaveChangesAsync();
        }

        public async Task AddUserToObject(Guid objectId, Guid userId, Guid permissionGroupId, Guid modifiedByUserId)
        {
            var securityDescriptor = await _dbContext.SecurityDescriptors.SingleAsync(x => x.ObjectId == objectId);
            await AddUserToSecurityDescriptor(securityDescriptor.SecurityDescriptorId, userId, permissionGroupId, modifiedByUserId);
        }

        public async Task AddRoleToObject(Guid objectId, Guid roleId, Guid permissionGroupId, Guid modifiedByUserId)
        {
            var securityDescriptor = await _dbContext.SecurityDescriptors.SingleAsync(x => x.ObjectId == objectId);
            await AddRoleToSecurityDescriptor(securityDescriptor.SecurityDescriptorId, roleId, permissionGroupId, modifiedByUserId);
        }

        public async Task AddUserToSecurityDescriptor(Guid securityDescriptorId, Guid userId, Guid permissionGroupId, Guid modifiedByUserId)
        {
            await _dbContext.SecurityDescriptorUserPermissions.AddAsync(new SecurityDescriptorUserPermission
            {
                SecurityDescriptorId = securityDescriptorId,
                CreatedTime = DateTime.UtcNow,
                PermissionGroupId = permissionGroupId,
                UsedId = userId,
                ModifiedByUserId = modifiedByUserId
            });
        }

        public async Task AddRoleToSecurityDescriptor(Guid securityDescriptorId, Guid roleId, Guid permissionGroupId, Guid modifiedByUserId)
        {
            await _dbContext.SecurityDescriptorRolePermissions.AddAsync(new SecurityDescriptorRolePermission
            {
                SecurityDescriptorId = securityDescriptorId,
                CreatedTime = DateTime.UtcNow,
                PermissionGroupId = permissionGroupId,
                RoleId = roleId,
                ModifiedByUserId = modifiedByUserId
            });
        }

        public async Task<SecurityDescriptor> CreateSecurityDescriptor(Guid objectId, Guid objectTypeId, Guid parentSecurityDescriptorId)
        {
            SecurityDescriptor securityDescriptor = new()
            {
                ObjectTypeId = objectTypeId,
                ObjectId = objectId,
                ParentSecurityDescriptorId = parentSecurityDescriptorId
            };
            await _dbContext.SecurityDescriptors.AddAsync(securityDescriptor);
            return securityDescriptor;
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