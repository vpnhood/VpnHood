using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using GrayMint.Common.AspNetCore.SimpleUserManagement;
using GrayMint.Common.AspNetCore.SimpleUserManagement.Dtos;
using VpnHood.AccessServer.DtoConverters;
using VpnHood.AccessServer.Dtos;
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
        var userId = userPrincipal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException("Could not find user's email claim!");
        return Guid.Parse(userId);
    }

    public async Task<Project[]> GetProjects(Guid userId)
    {
        var userRoles = await _simpleRoleProvider.GetUserRoles(userId: userId);
        var projectIds = userRoles.Where(x => x.AppId != "*").Select(x => Guid.Parse(x.AppId!));
        var projects = _vhContext.Projects
            .Where(x => projectIds.Contains(x.ProjectId))
            .ToArray();

        var ret = projects.Select(x => x.ToDto()).ToArray();
        return ret;
    }

    public async Task Register(string email, string? firstName, string? lastName)
    {
        await _simpleUserProvider.Create(new UserCreateRequest(email)
        {
            FirstName = firstName,
            LastName = lastName,
            Description = null
        });
    }
}