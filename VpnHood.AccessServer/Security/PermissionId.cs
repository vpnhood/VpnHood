using System.Linq;
using System.Threading.Tasks;
using VpnHood.AccessServer.Auth.Models;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Security
{
    public static class SecurityUtil
    {
        private static PermissionId[] SystemPermissions => SystemAdminPermissions;
        private static PermissionId[] SystemAdminPermissions => new[]{
            PermissionId.ExportCertificate
        };

        public static async Task Init()
        {
            await using VhContext vhContext = new();
            await vhContext.Init<ObjectTypeId, PermissionId, PermissionGroupId>();

            vhContext.PermissionGroupPermissions.RemoveRange(vhContext.PermissionGroupPermissions);
            vhContext.PermissionGroupPermissions.AddRange(SystemPermissions.Select(x => new PermissionGroupPermission { PermissionGroupId = (int)PermissionGroupId.System, PermissionId = (int)x }));
            vhContext.PermissionGroupPermissions.AddRange(SystemAdminPermissions.Select(x => new PermissionGroupPermission { PermissionGroupId = (int)PermissionGroupId.SystemAdmin, PermissionId = (int)x }));
        }
    }


    public enum PermissionId
    {
        ExportCertificate = 1
    }
}