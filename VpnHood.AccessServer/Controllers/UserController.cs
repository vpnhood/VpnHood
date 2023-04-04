using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers;

[ApiController]
[Route("/api/v{version:apiVersion}/users")]
public class UserController : ControllerBase
{
    private readonly UserService _userService;
    public UserController(UserService userService)
    {
        _userService = userService;
    }

    [Authorize]
    [HttpPost("current/register")]
    public Task RegisterCurrentUser()
    {
        var email =
            User.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Email)?.Value.ToLower()
            ?? throw new UnauthorizedAccessException("Could not find user's email claim!");

        return _userService.Register(
            email,
            User.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.GivenName)?.Value,
            User.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Surname)?.Value);
    }

    [Authorize]
    [HttpGet("current")]
    public async Task<User> GetCurrentUser()
    {
        await _userService.CheckRegistered(User);
        return await _userService.GetUser(UserService.GetUserId(User));
    }

    [Authorize]
    [HttpGet("current/projects")]
    public async Task<Project[]> GetProjects()
    {
        await _userService.CheckRegistered(User);
        return await _userService.GetProjects(UserService.GetUserId(User));
    }
}