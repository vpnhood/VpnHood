using System;

namespace VpnHood.AccessServer.Auth.Models
{
    public class PermissionGroupPermission
    {
        public Guid PermissionGroupId { get; set; }
        public int PermissionId { get; set; }
    }
}