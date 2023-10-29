using GrayMint.Authorization.PermissionAuthorizations;

namespace VpnHood.AccessServer.Security;

public class AuthorizeProjectPermissionAttribute : AuthorizePermissionAttribute
{
    public AuthorizeProjectPermissionAttribute(string permission) : base(permission)
    {
        ResourceRoute = "{projectId}";
    }
}