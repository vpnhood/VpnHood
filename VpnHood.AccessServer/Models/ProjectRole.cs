using System;
using VpnHood.AccessServer.Authorization.Models;

namespace VpnHood.AccessServer.Models;

public class ProjectRole
{
    public Guid ProjectRoleId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RoleId { get; set; }

    public virtual Project? Project { get; set; }
    public virtual Role? Role { get; set; }
}