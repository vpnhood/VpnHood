using System.ComponentModel.DataAnnotations.Schema;

namespace VpnHood.AccessServer.Auth.Models
{
    public class Permission
    {
        public int PermissionId { get; set; }
        public string PermissionName { get; set; } = null!;
    }
}