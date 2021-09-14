using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Auth.Models;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Security
{
    public class SecurityManager : IDisposable, IAsyncDisposable
    {
        private readonly VhContext _vhContext;
        private readonly bool _autoDisposeContext;

        private static PermissionId[] SystemPermissions => SystemAdminPermissions;
        private static PermissionId[] SystemAdminPermissions => new[]{
            PermissionId.ExportCertificate
        };

        public static Guid SystemSecurityDescriptorId { get; } = Guid.Parse("{C0133593-DAFE-4667-BA0F-0823DD40947D}");
        public static Guid SystemObjectId { get; } = Guid.Parse("{186C4064-0B9E-4840-BCB0-6FAECE4B3E7E}");
        public static Guid SystemUserId { get; } = Guid.Parse("{186C4064-0B9E-4840-BCB0-6FAECE4B3E7E}");

        public SecurityManager(VhContext vhContext, bool autoDisposeContext = false)
        {
            _vhContext = vhContext;
            _autoDisposeContext = autoDisposeContext;
        }

        public async Task Init()
        {
            await _vhContext.Init<ObjectTypeId, PermissionId>();

            // init PermissionGroups
            var permissionGroups = await _vhContext.PermissionGroups.ToArrayAsync();
            foreach (var item in PermissionGroupId.All)
                if (permissionGroups.All(x => x.PermissionGroupId != item.PermissionGroupId))
                    await _vhContext.AddAsync(item);

            // Recreate PermissionGroupPermissions
            _vhContext.PermissionGroupPermissions.RemoveRange(_vhContext.PermissionGroupPermissions);
            _vhContext.PermissionGroupPermissions.AddRange(SystemPermissions.Select(x => new PermissionGroupPermission { PermissionGroupId = PermissionGroupId.System, PermissionId = (int)x }));
            _vhContext.PermissionGroupPermissions.AddRange(SystemAdminPermissions.Select(x => new PermissionGroupPermission { PermissionGroupId = PermissionGroupId.SystemAdmin, PermissionId = (int)x }));

            //init SystemUser
            if (await _vhContext.Users.AllAsync(x => x.UserId != SystemUserId))
                await _vhContext.Users.AddAsync(new User { UserId = SystemUserId, UserName = "System" });

            //init SystemUser permissions
            if (await _vhContext.SecurityDescriptors.AllAsync(x => x.SecurityDescriptorId != SystemSecurityDescriptorId))
            {
                SecurityDescriptor securityDescriptor = new()
                {
                    SecurityDescriptorId = SystemSecurityDescriptorId,
                    ObjectTypeId = (int)ObjectTypeId.System,
                    ObjectId = SystemObjectId,
                    ParentSecurityDescriptorId = null
                };
                await _vhContext.SecurityDescriptors.AddAsync(securityDescriptor);
                await AddUserToSecurityDescriptor(securityDescriptor.SecurityDescriptorId, SystemUserId, PermissionGroupId.System, SystemUserId);
            }

            await _vhContext.SaveChangesAsync();
        }

        public async Task AddUserToObject(Guid objectId, Guid userId, Guid permissionGroupId, Guid modifiedByUserId)
        {
            var securityDescriptor = await _vhContext.SecurityDescriptors.SingleOrDefaultAsync(x => x.ObjectId == objectId);
            await AddUserToSecurityDescriptor(securityDescriptor.SecurityDescriptorId, userId, permissionGroupId, modifiedByUserId);
        }

        public async Task AddUserToSecurityDescriptor(Guid securityDescriptorId, Guid userId, Guid permissionGroupId, Guid modifiedByUserId)
        {
            await _vhContext.SecurityDescriptorUserPermissions.AddAsync(new SecurityDescriptorUserPermission
            {
                SecurityDescriptorId = securityDescriptorId,
                CreatedTime = DateTime.UtcNow,
                PermissionGroupId = permissionGroupId,
                UsedId = userId,
                ModifiedByUserId = modifiedByUserId
            });
        }


        public async Task<SecurityDescriptor> CreateSecurityDescriptor(Guid objectId, ObjectTypeId objectTypeId, Guid parentSecurityDescriptorId)
        {
            SecurityDescriptor securityDescriptor = new()
            {
                ObjectTypeId = (int)objectTypeId,
                ObjectId = objectId,
                ParentSecurityDescriptorId = parentSecurityDescriptorId
            };
            await _vhContext.SecurityDescriptors.AddAsync(securityDescriptor);
            return securityDescriptor;
        }


        public void Dispose()
        {
            if (_autoDisposeContext)
                _vhContext.Dispose();
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            if (_autoDisposeContext)
                await _vhContext.DisposeAsync();
        }
    }
}