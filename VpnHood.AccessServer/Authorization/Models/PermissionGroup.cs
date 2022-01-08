using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Authorization.Models;

public class PermissionGroup
{
    public Guid PermissionGroupId { get; set; }
    public string PermissionGroupName { get; set; }

    [JsonIgnore] public virtual ICollection<Permission> Permissions { get; set; } = new HashSet<Permission>();
    [JsonIgnore] public virtual ICollection<PermissionGroupPermission> PermissionGroupPermissions { get; set; } = new HashSet<PermissionGroupPermission>();

    public PermissionGroup(Guid permissionGroupId, string permissionGroupName)
    {
        PermissionGroupId = permissionGroupId;
        PermissionGroupName = permissionGroupName;
    }
}