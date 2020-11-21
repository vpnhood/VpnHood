using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Threading.Tasks;
using VpnHood.AccessServer.Models;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AccessTokenController : SuperController<AccessTokenController>
    {
        public AccessTokenController(ILogger<AccessTokenController> logger) : base(logger)
        {
        }

        [HttpPost]
        [Route(nameof(CreatePublic))]
        public async Task<AccessToken> CreatePublic(string serverEndPoint, string tokenName, int maxTraffic)
        {
            Authorize(App.AdminUserId);

            var accessTokenService = await AccessTokenService.CreatePublic(serverEndPoint: serverEndPoint, tokenName: tokenName, maxTraffic: maxTraffic);
            return await accessTokenService.GetAccessToken();
        }

        [HttpPost]
        [Route(nameof(CreatePrivate))]
        public async Task<AccessToken> CreatePrivate(string serverEndPoint, string tokenName, int maxTraffic, int maxClient, DateTime? endTime = null, int lifetime = 0)
        {
            Authorize(App.AdminUserId);

            var accessTokenService = await AccessTokenService.CreatePrivate(
                serverEndPoint: serverEndPoint, tokenName: tokenName, maxTraffic: maxTraffic, 
                maxClient: maxClient, endTime: endTime, lifetime: lifetime);
            return await accessTokenService.GetAccessToken();
        }

        [HttpGet]
        public Task<AccessToken> Get(Guid accessTokenId)
        {
            Authorize(App.AdminUserId);

            var accessTokenService = AccessTokenService.FromId(accessTokenId);
            return accessTokenService.GetAccessToken();
        }
    }
}
