using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    public class SuperController<T> : ControllerBase
    {
        protected readonly ILogger<T> _logger;

        protected SuperController(ILogger<T> logger)
        {
            _logger = logger;
        }

        protected string UserId
        {
            get
            {
                var issuer = User.Claims.FirstOrDefault(claim => claim.Type == "iss")?.Value ??
                             throw new UnauthorizedAccessException();
                var sub = User.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.NameIdentifier)?.Value ??
                          throw new UnauthorizedAccessException();
                return issuer + ":" + sub;
            }
        }

        protected void Authorize(string userId)
        {
            if (UserId != userId)
                throw new UnauthorizedAccessException();
        }
    }
}