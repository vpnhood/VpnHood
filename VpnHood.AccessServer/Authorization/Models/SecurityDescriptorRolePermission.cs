using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Authorization.Models
{
    public class SecurityDescriptorRolePermission
    {
        public Guid SecurityDescriptorId { get; set; }
        public Guid RoleId { get; set; }
        public Guid PermissionGroupId { get; set; }
        public Guid ModifiedByUserId { get; set; }
        public DateTime CreatedTime { get; set; }

        [JsonIgnore] public virtual SecurityDescriptor? SecurityDescriptor { get; set; }

    }
}