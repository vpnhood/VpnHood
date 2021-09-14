using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VpnHood.AccessServer.Auth.Models
{
    public class SecurityDescriptorUserPermission
    {
        public Guid SecurityDescriptorId { get; set; }
        public Guid UsedId { get; set; }
        public Guid PermissionGroupId { get; set; }
        public Guid ModifiedByUserId { get; set; }
        public DateTime CreatedTime { get; set; }
    }
}