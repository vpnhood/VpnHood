using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VpnHood.AccessServer.Auth.Models
{
    public class PermissionGroup<TPermissionGroup> where TPermissionGroup : Enum
    {
        public TPermissionGroup PermissionGroupId { get; set; } = default!;
        public string PermissionGroupName { get; set; } = null!;
    }
}