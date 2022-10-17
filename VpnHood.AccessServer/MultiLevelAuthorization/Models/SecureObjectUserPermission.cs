using System;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.MultiLevelAuthorization.Models;

public class SecureObjectUserPermission
{
    public Guid SecureObjectId { get; set; }
    public Guid UserId { get; set; }
    public Guid PermissionGroupId { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public DateTime CreatedTime { get; set; }
        
    [JsonIgnore] public virtual SecureObject? SecureObject { get; set; }
}