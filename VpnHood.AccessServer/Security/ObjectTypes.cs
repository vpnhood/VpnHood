using VpnHood.AccessServer.Authorization;
using VpnHood.AccessServer.Authorization.Models;

namespace VpnHood.AccessServer.Security
{
    public static class ObjectTypes
    {
        public static ObjectType System { get; } = AuthManager.SystemObjectType;
        public static ObjectType[] All { get; } = { System };

    }
}