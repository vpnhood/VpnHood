using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VpnHood.AccessServer.Auth.Models
{
    public class RoleUser
    {
        public Guid RoleId { get; set; }
        public Guid UserId { get; set; } 
        public Guid ModifiedByUserId { get; set; }
        public DateTime CreatedTime { get; set; }
    }
}