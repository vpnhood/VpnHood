using System;

namespace VpnHood.AccessServer.Models
{
    public partial class User
    {
        public Guid UserId { get; set; } 
        public string AuthUserId { get; set; } = null!;
    }
}
