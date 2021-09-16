using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Authorization.Models
{
    public class SecurityDescriptor
    {
        public Guid SecurityDescriptorId { get; set; }
        public Guid ObjectId { get; set; }
        public Guid ObjectTypeId { get; set; }
        public Guid? ParentSecurityDescriptorId { get; set; }

        public virtual ObjectType? ObjectType { get; set; }
        public virtual SecurityDescriptor? ParentSecurityDescriptor { get; set; }
        [JsonIgnore] public virtual ICollection<SecurityDescriptorRolePermission>? RolePermissions { get; set; }
        [JsonIgnore] public virtual ICollection<SecurityDescriptorUserPermission>? UserPermissions { get; set; }
        [JsonIgnore] public virtual ICollection<SecurityDescriptor>? SecurityDescriptors { get; set; }

    }
}