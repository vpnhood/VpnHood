using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
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


        [HttpPost]
        [Route("addusage")]
        public async Task<Access> AddUsage(AddUsageParams addUsageParams)
        {
            _logger.LogInformation($"AddUsage for {addUsageParams.ClientIdentity}, SentTraffic: {addUsageParams.SentTrafficByteCount / 1000000} MB, ReceivedTraffic: {addUsageParams.ReceivedTrafficByteCount / 1000000} MB");

            var clientIdentity = addUsageParams.ClientIdentity;
            var tokenService = TokenService.FromId(clientIdentity.TokenId);
            var token = await tokenService.GetToken();
            var clientIp = token.isPublic ? clientIdentity.ClientIp.ToString() : "*";

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
        public Task<Access> GetAccess(ClientIdentity clientIdentity)
        {
            return AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity });
        }

    }
}
