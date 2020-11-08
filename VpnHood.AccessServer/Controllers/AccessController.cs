using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.HttpSys;
using Microsoft.AspNetCore.Server.IIS;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public partial class AccessController : ControllerBase, IAccessServer
    {
        private readonly ILogger<AccessController> _logger;

        public AccessController(ILogger<AccessController> logger)
        {
            _logger = logger;
        }

        private string UserId
        {
            get
            {
                var issuer = User.Claims.FirstOrDefault(claim => claim.Type == "iss")?.Value ?? throw new UnauthorizedAccessException();
                var sub = User.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.NameIdentifier)?.Value ?? throw new UnauthorizedAccessException();
                return issuer + ":" + sub;
            }
        }


        [HttpPost]
        [Route("addusage")]
        public async Task<Access> AddUsage(AddUsageParams addUsageParams)
        {
            if (UserId != App.AgentUserId)
                throw new UnauthorizedAccessException();

            var clientIdentity = addUsageParams.ClientIdentity ?? throw new ArgumentNullException(nameof(addUsageParams.ClientIdentity));
            _logger.LogInformation($"AddUsage for {addUsageParams.ClientIdentity}, SentTraffic: {addUsageParams.SentTrafficByteCount / 1000000} MB, ReceivedTraffic: {addUsageParams.ReceivedTrafficByteCount / 1000000} MB");

            var tokenService = TokenService.FromId(clientIdentity.TokenId);
            var token = await tokenService.GetToken();

            // set clientIp
            var clientIp = "*";
            if (token.isPublic)
                clientIp = !string.IsNullOrEmpty(clientIdentity.ClientIp) ? clientIdentity.ClientIp : throw new ArgumentNullException(nameof(clientIdentity.ClientIp));

            // add usage
            var accessUsage = await tokenService.AddAccessUsage(
                clientIp: clientIp,
                sentTraffic: addUsageParams.SentTrafficByteCount,
                receivedTraffic: addUsageParams.ReceivedTrafficByteCount);

            // create return
            using var md5 = MD5.Create();
            var accessId = Convert.ToBase64String(md5.ComputeHash(Encoding.UTF8.GetBytes(token.tokenId + "_" + clientIp)));
            var access = new Access()
            {
                AccessId = accessId,
                DnsName = token.dnsName,
                ServerEndPoint = token.serverEndPoint,
                Secret = token.secret,
                ExpirationTime = token.endTime,
                MaxClientCount = token.maxClient,
                MaxTrafficByteCount = token.maxTraffic,
                ReceivedTrafficByteCount = accessUsage.receivedTraffic,
                SentTrafficByteCount = accessUsage.sentTraffic,
            };

            // set expiration time on first use
            if (access.ExpirationTime == null && access.SentTrafficByteCount != 0 && access.ReceivedTrafficByteCount != 0 && token.lifetime != 0)
            {
                access.ExpirationTime = DateTime.Now.AddDays(token.lifetime);
                _logger.LogInformation($"Access has been activated! Expiration: {access.ExpirationTime}, ClientIdentity: {clientIdentity}");
            }

            // calculate status
            if (access.ExpirationTime < DateTime.Now)
                access.StatusCode = AccessStatusCode.Expired;
            else if (access.SentTrafficByteCount + access.ReceivedTrafficByteCount > token.maxTraffic)
                access.StatusCode = AccessStatusCode.TrafficOverflow;
            else
                access.StatusCode = AccessStatusCode.Ok;

            return access;
        }

        [HttpGet]
        [Route("getaccess")]
        public Task<Access> GetAccess(ClientIdentity clientIdentity)
        {
            if (UserId != App.AgentUserId)
                throw new UnauthorizedAccessException();

            return AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity });
        }

    }
}
