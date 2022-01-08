using System;

namespace VpnHood.AccessServer.Authorization.Models;

public class PermissionGroupPermission
{
    public Guid PermissionGroupId { get; set; }
    public int PermissionId { get; set; }

    public virtual PermissionGroup? PermissionGroup { get; set; }
    public virtual Permission? Permission { get; set; }

}