using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.MultiLevelAuthorization.Models;

public class SecureObject
{
    public Guid SecureObjectId { get; set; }
    public Guid SecureObjectTypeId { get; set; }
    public Guid? ParentSecureObjectId { get; set; }

    public virtual SecureObjectType? SecureObjectType { get; set; }
    public virtual SecureObject? ParentSecureObject { get; set; }
    [JsonIgnore] public virtual ICollection<SecureObjectRolePermission>? RolePermissions { get; set; }
    [JsonIgnore] public virtual ICollection<SecureObjectUserPermission>? UserPermissions { get; set; }
    [JsonIgnore] public virtual ICollection<SecureObject>? SecureObjects { get; set; }

}