using System;

namespace VpnHood.AccessServer.MultiLevelAuthorization.Models;

public class Role
{
    public Guid RoleId { get; set; }
    public string RoleName { get; set; } = null!;
    public Guid OwnerSecureObjectId { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public DateTime CreatedTime { get; set; }

}