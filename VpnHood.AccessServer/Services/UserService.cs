using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore.SimpleUserManagement;
using GrayMint.Common.AspNetCore.SimpleUserManagement.Dtos;
using GrayMint.Common.Exceptions;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Dtos.UserDtos;
using VpnHood.AccessServer.Exceptions;
using VpnHood.AccessServer.Persistence;

namespace VpnHood.AccessServer.Services;

public class UserService
{
    private readonly SimpleRoleProvider _simpleRoleProvider;
    private readonly SimpleUserProvider _simpleUserProvider;
    private readonly VhContext _vhContext;

    public UserService(
        VhContext vhContext,
        SimpleRoleProvider simpleRoleProvider,
        SimpleUserProvider simpleUserProvider)
    {
        _simpleRoleProvider = simpleRoleProvider;
        _simpleUserProvider = simpleUserProvider;
        _vhContext = vhContext;
    }

    public static Guid GetUserId(ClaimsPrincipal userPrincipal)
    {
        var userId = userPrincipal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new NotExistsException("User has not been registered.");
        if (!Guid.TryParse(userId, out var ret))
            throw new UnregisteredUser("User has not been registered.");
        return ret;
    }

    public async Task CheckRegistered(ClaimsPrincipal claimsPrincipal)
    {
        var simpleUser = await _simpleUserProvider.FindSimpleUser(claimsPrincipal);
        if (simpleUser==null)
            throw new UnregisteredUser("User has not been registered.");
    }

    public async Task<User> GetUser(Guid userId)
    {
        var user = await _simpleUserProvider.Get<UserExData>(userId);
        return user.ToDto();
    }

    public async Task<Project[]> GetProjects(Guid userId)
    {
        var userRoles = await _simpleRoleProvider.GetUserRoles(userId: userId);
        var projectIds = userRoles.Where(x => x.AppId != "*").Select(x => Guid.Parse(x.AppId));
        var projects = _vhContext.Projects
            .Where(x => projectIds.Contains(x.ProjectId))
            .ToArray();

        var ret = projects.Select(x => x.ToDto()).ToArray();
        return ret;
    }

    public async Task Register(string email, string? firstName, string? lastName)
    {
        await _simpleUserProvider.Create(new UserCreateRequest
        {
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            Description = null
        });
    }
}