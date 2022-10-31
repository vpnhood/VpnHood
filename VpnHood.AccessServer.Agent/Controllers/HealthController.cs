using GrayMint.Common.AspNetCore.Auth.BotAuthentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VpnHood.AccessServer.Agent.Persistence;
using VpnHood.AccessServer.Agent.Services;
// ReSharper disable NotAccessedField.Local

namespace VpnHood.AccessServer.Agent.Controllers;

public class HealthController : ControllerBase
{
    private readonly VhContext _vhContext;
    private readonly CacheService _cacheService;
    private readonly IBotAuthenticationProvider _botAuthenticationProvider;
    private readonly ILogger<SystemController> _logger;

    public HealthController(VhContext vhContext, CacheService cacheService, ILogger<SystemController> logger, IBotAuthenticationProvider botAuthenticationProvider)
    {
        _vhContext = vhContext;
        _cacheService = cacheService;
        _logger = logger;
        _botAuthenticationProvider = botAuthenticationProvider;
    }

    [HttpGet("Foo")]
    [AllowAnonymous]
    public async Task<string> Foo()
    {
        await _cacheService.GetServer(Guid.Parse("c3520778-dba2-4b70-a91d-a602b000734d"));

        await Task.Delay(0);
        _logger.LogInformation("");
        _logger.LogInformation("_______________________");
        _logger.LogInformation("Information log");
        _logger.LogWarning("Warning log");
        //_logger.LogError("Error log");
        return "OK";
    }
}
