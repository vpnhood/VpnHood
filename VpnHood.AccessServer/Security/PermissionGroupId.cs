using System;
using VpnHood.AccessServer.Auth.Models;

namespace VpnHood.AccessServer.Security
{
    public static class PermissionGroupId
    {
        public static Guid System { get; } = Guid.Parse("{1543EDF1-804C-412F-A676-8791827EFBCC}");
        public static Guid SystemAdmin { get; } = Guid.Parse("{2CC87D7A-8B83-4949-A6A4-66485F64A786}");

        public static PermissionGroup[] All { get; } = {
            new() {PermissionGroupId = System, PermissionGroupName = nameof(System)},
            new() {PermissionGroupId = SystemAdmin, PermissionGroupName = nameof(SystemAdmin)},
        };
    }
}