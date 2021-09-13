using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VpnHood.AccessServer.Auth.Models
{
    public class SecurityDescriptorRolePermission
    {
        public int SecurityDescriptorId { get; set; }
        public Guid RoleId { get; set; }
        public int PermissionGroupId { get; set; }
        public Guid ModifiedByUserId { get; set; }
        public DateTime CreatedTime { get; set; }
    }
}