using System;
using VpnHood.AccessServer.Authorization;
using VpnHood.AccessServer.Authorization.Models;

namespace VpnHood.AccessServer.Security
{
    public static class SecureObjectTypes
    {
        public static SecureObjectType System { get; } = AuthManager.SystemSecureObjectType;
        public static SecureObjectType Project { get; } = new(Guid.Parse("{6FE94D89-632D-40C9-9176-30878F830AEE}"), nameof(Project));

        public static SecureObjectType[] All { get; } = { System, Project };
    }
}