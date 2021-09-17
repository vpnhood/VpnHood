using System;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Authorization.Models
{
    public class RoleUser
    {
        public Guid RoleId { get; set; }
        public Guid UserId { get; set; } 
        public Guid ModifiedByUserId { get; set; }
        public DateTime CreatedTime { get; set; }

        [JsonIgnore] public virtual Role? Role { get; set; }
    }
}