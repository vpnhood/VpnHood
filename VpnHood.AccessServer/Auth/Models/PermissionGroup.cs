using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VpnHood.AccessServer.Auth.Models
{
    public class PermissionGroup
    {
        public Guid PermissionGroupId { get; set; }
        public string PermissionGroupName { get; set; } = null!;
    }
}