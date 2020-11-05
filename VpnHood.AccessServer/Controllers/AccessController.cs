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
            var clientIp = token.isPublic ? "*" : clientIdentity.ClientIp.ToString();
            var accessUsage = await tokenService.GetAccessUsage(clientIp);

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


            return access;
        }


        [HttpGet]
        public Task<Access> GetAccess(ClientIdentity clientIdentity)
        {
            return AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity });
        }
    
    }
}
