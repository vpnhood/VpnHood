using System.ComponentModel.DataAnnotations.Schema;

namespace VpnHood.AccessServer.Auth.Models
{
    public class PermissionGroupPermission
    {
        public int PermissionGroupId { get; set; }
        public int PermissionId { get; set; }
    }
}