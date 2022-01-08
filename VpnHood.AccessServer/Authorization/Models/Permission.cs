using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Authorization.Models;

public class Permission
{
    public int PermissionId { get; set; }
    public string PermissionName { get; set; }

    public Permission(int permissionId, string permissionName)
    {
        PermissionId = permissionId;
        PermissionName = permissionName;
    }

    [JsonIgnore] public virtual ICollection<PermissionGroup>? PermissionGroups { get; set; }
    [JsonIgnore] public virtual ICollection<PermissionGroupPermission>? PermissionGroupPermissions { get; set; }
        
}