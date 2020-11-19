using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
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
        [Route("addusage")]
        public async Task<Access> AddUsage(AddUsageParams addUsageParams)
        {
            Authorize(App.VpnServerUserId);

            var clientIdentity = addUsageParams.ClientIdentity ?? throw new ArgumentNullException(nameof(addUsageParams), $"{nameof(addUsageParams.ClientIdentity)} has not been initialized!");
            _logger.LogInformation($"AddUsage for {addUsageParams.ClientIdentity}, SentTraffic: {addUsageParams.SentTrafficByteCount / 1000000} MB, ReceivedTraffic: {addUsageParams.ReceivedTrafficByteCount / 1000000} MB");

            var tokenService = AccessTokenService.FromId(clientIdentity.TokenId);
            var accssToken = await tokenService.GetAccessToken();

            // set clientIp
            var clientIp = "*";
            if (accssToken.isPublic)
                clientIp = !string.IsNullOrEmpty(clientIdentity.ClientIp) ? clientIdentity.ClientIp : throw new ArgumentNullException(nameof(addUsageParams), $"{nameof(clientIdentity.ClientIp)} has not been initialized!");

            // add usage
            var accessUsage = await tokenService.AddAccessUsage(
                clientIp: clientIp,
                sentTraffic: addUsageParams.SentTrafficByteCount,
                receivedTraffic: addUsageParams.ReceivedTrafficByteCount);

            // create return
            using var md5 = MD5.Create();
            var accessId = Convert.ToBase64String(md5.ComputeHash(Encoding.UTF8.GetBytes(accssToken.accessTokenId + "_" + clientIp)));
            var access = new Access()
            {
                AccessId = accessId,
                ServerEndPoint = accssToken.serverEndPoint,
                Secret = accssToken.secret,
                ExpirationTime = accssToken.endTime,
                MaxClientCount = accssToken.maxClient,
                MaxTrafficByteCount = accssToken.maxTraffic,
                ReceivedTrafficByteCount = accessUsage.receivedTraffic,
                SentTrafficByteCount = accessUsage.sentTraffic,
            };

            // set expiration time on first use
            if (access.ExpirationTime == null && access.SentTrafficByteCount != 0 && access.ReceivedTrafficByteCount != 0 && accssToken.lifetime != 0)
            {
                access.ExpirationTime = DateTime.Now.AddDays(accssToken.lifetime);
                _logger.LogInformation($"Access has been activated! Expiration: {access.ExpirationTime}, ClientIdentity: {clientIdentity}");
            }

            // calculate status
            if (access.ExpirationTime < DateTime.Now)
                access.StatusCode = AccessStatusCode.Expired;
            else if (access.SentTrafficByteCount + access.ReceivedTrafficByteCount > accssToken.maxTraffic)
                access.StatusCode = AccessStatusCode.TrafficOverflow;
            else
                access.StatusCode = AccessStatusCode.Ok;

            return access;
        }

        [HttpGet]
        [Route("getaccess")]
        public Task<Access> GetAccess(ClientIdentity clientIdentity)
        {
            Authorize(App.VpnServerUserId);
            return AddUsage(new AddUsageParams() { ClientIdentity = clientIdentity });
        }

        [HttpGet]
        [Route("getSslCertificateData")]
        public async Task<byte[]> GetSslCertificateData(string serverEndPoint)
        {
            Authorize(App.VpnServerUserId);

            try
            {
                var certificateService = CertificateService.FromId(serverEndPoint);
                var res = await certificateService.Get();
                return res.rawData;
            }
            catch (KeyNotFoundException)
            {
                var certificateService = await CertificateService.Create(serverEndPoint, null);
                var res = await certificateService.Get();
                return res.rawData;
            }
        }

        public Task<byte[]> GetSslCertificateData(string serverId, string serverIp)
        {
            //todo remove
            throw new NotImplementedException();
        }
    }
}
