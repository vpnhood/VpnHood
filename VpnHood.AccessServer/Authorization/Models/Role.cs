using System;

namespace VpnHood.AccessServer.Authorization.Models
{
    public class Role
    {
        public Guid RoleId { get; set; }
        public string RoleName { get; set; } = null!;
        public Guid ModifiedByUserId { get; set; }
        public DateTime CreatedTime { get; set; }

    }
}