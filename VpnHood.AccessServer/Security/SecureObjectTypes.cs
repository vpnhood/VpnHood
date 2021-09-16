using VpnHood.AccessServer.Authorization;
using VpnHood.AccessServer.Authorization.Models;

namespace VpnHood.AccessServer.Security
{
    public static class SecureObjectTypes
    {
        public static SecureObjectType System { get; } = AuthManager.SystemSecureObjectType;
        public static SecureObjectType[] All { get; } = { System };

    }
}