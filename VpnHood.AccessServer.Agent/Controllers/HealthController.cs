using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VpnHood.AccessServer.Agent.Persistence;
using VpnHood.AccessServer.Agent.Services;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.Agent.Controllers;

public class HealthController : ControllerBase
{
    private readonly VhContext _vhContext;
    private readonly CacheService _cache;
    private readonly ILogger<SystemController> _logger;

    public HealthController(VhContext vhContext, CacheService cache, ILogger<SystemController> logger)
    {
        _vhContext = vhContext;
        _cache = cache;
        _logger = logger;
    }

    [HttpGet("Foo")]
    [AllowAnonymous]
    public async Task<string> Foo()
    {
        var a = await _vhContext.Sessions.SingleOrDefaultAsync(x => x.SessionId == 10);
        await _cache.SaveChanges();
        var device = new Device(Guid.Parse("{5E9035A7-2C6C-4C64-8B14-D700334DCA46}"));
        _vhContext.Attach(device);

        await Task.Delay(0);
        _logger.LogInformation("");
        _logger.LogInformation("_______________________");
        _logger.LogInformation("Information log");
        _logger.LogWarning("Warning log");
        //_logger.LogError("Error log");
        return "OK";
    }
}
