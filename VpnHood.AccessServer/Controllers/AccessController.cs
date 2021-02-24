using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Services;
using VpnHood.Server;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AccessController : SuperController<AccessController>, IAccessServer
    {
        public AccessController(ILogger<AccessController> logger) : base(logger)
        {
        }

        [HttpPost]
        [Route(nameof(AddUsage))]
        [Authorize(AuthenticationSchemes = "auth", Roles = "Admin, VpnServer")]
        public async Task<Access> AddUsage(AddUsageParams addUsageParams)
        {
            var clientIdentity = addUsageParams.ClientIdentity ?? throw new ArgumentNullException(nameof(addUsageParams), $"{nameof(addUsageParams.ClientIdentity)} has not been initialized!");
            _logger.LogInformation($"AddUsage for {addUsageParams.ClientIdentity}, SentTraffic: {addUsageParams.SentTrafficByteCount / 1000000} MB, ReceivedTraffic: {addUsageParams.ReceivedTrafficByteCount / 1000000} MB");

            var tokenService = AccessTokenService.FromId(clientIdentity.TokenId);
            var accessToken = await tokenService.GetAccessToken();

            // set clientIp
            var clientIp = "*";
            if (accessToken.isPublic)
                clientIp = !string.IsNullOrEmpty(clientIdentity.ClientIp) ? clientIdentity.ClientIp : throw new ArgumentNullException(nameof(addUsageParams), $"{nameof(clientIdentity.ClientIp)} has not been initialized!");

            // add usage
            var accessUsage = await tokenService.AddAccessUsage(
                clientId: clientIdentity.ClientId,
                clientIp: clientIp,
                clientVersion: clientIdentity.ClientVersion,
                sentTraffic: addUsageParams.SentTrafficByteCount,
                receivedTraffic: addUsageParams.ReceivedTrafficByteCount);

            // create return
            using var md5 = MD5.Create();
            var accessId = Convert.ToBase64String(md5.ComputeHash(Encoding.UTF8.GetBytes(accessToken.accessTokenId + "_" + clientIp)));
            var access = new Access()
            {
                AccessId = accessId,
                ServerEndPoint = accessToken.serverEndPoint,
                Secret = accessToken.secret,
                ExpirationTime = accessToken.endTime,
                MaxClientCount = accessToken.maxClient,
                MaxTrafficByteCount = accessToken.maxTraffic,
                ReceivedTrafficByteCount = accessUsage.cycleReceivedTraffic,
                SentTrafficByteCount = accessUsage.cycleSentTraffic,
            };

            // set expiration time on first use
            if (access.ExpirationTime == null && access.SentTrafficByteCount != 0 && access.ReceivedTrafficByteCount != 0 && accessToken.lifetime != 0)
            {
                access.ExpirationTime = DateTime.Now.AddDays(accessToken.lifetime);
                _logger.LogInformation($"Access has been activated! Expiration: {access.ExpirationTime}, ClientIdentity: {clientIdentity}");
            }

            // calculate status
            if (access.ExpirationTime < DateTime.Now)
                access.StatusCode = AccessStatusCode.Expired;
            else if (accessToken.maxTraffic != 0 && access.SentTrafficByteCount + access.ReceivedTrafficByteCount > accessToken.maxTraffic)
                access.StatusCode = AccessStatusCode.TrafficOverflow;
            else
                access.StatusCode = AccessStatusCode.Ok;

            return access;
        }

        [HttpGet]
        [Authorize(AuthenticationSchemes = "auth", Roles = "Admin, VpnServer")]
        [Route(nameof(GetAccess))]
        public Task<Access> GetAccess(ClientIdentity clientIdentity)
        {
            return AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity });
        }

        [HttpGet]
        [Authorize(AuthenticationSchemes = "auth", Roles = "Admin, VpnServer")]
        [Route(nameof(GetSslCertificateData))]
        public async Task<byte[]> GetSslCertificateData(string serverEndPoint)
        {
            var certificateService = CertificateService.FromId(serverEndPoint);
            var res = await certificateService.Get();
            return res.rawData;
        }
    }
}
