using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VpnHood.AccessServer.Auth.Models
{
    public class Role
    {
        public Guid RoleId { get; set; }
        public string RoleName { get; set; } = null!;
        public Guid ModifiedByUserId { get; set; }
        public DateTime CreatedTime { get; set; }
    }
}