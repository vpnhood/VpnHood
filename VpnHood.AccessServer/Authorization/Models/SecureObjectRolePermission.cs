using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Authorization.Models
{
    public class SecureObjectRolePermission
    {
        public Guid SecureObjectId { get; set; }
        public Guid RoleId { get; set; }
        public Guid PermissionGroupId { get; set; }
        public Guid ModifiedByUserId { get; set; }
        public DateTime CreatedTime { get; set; }

        [JsonIgnore] public virtual SecureObject? SecureObject { get; set; }
        [JsonIgnore] public virtual Role? Role { get; set; }
        [JsonIgnore] public virtual PermissionGroup? PermissionGroup { get; set; }
    }
}