using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Services;
using VpnHood.Server;

//todo use nuget

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
        public async Task<Access> AddUsage(UsageParams usageParams)
        {
            if (usageParams.ClientIdentity == null) throw new ArgumentException($"{nameof(usageParams.ClientIdentity)} should not be null", nameof(usageParams));
            if (usageParams.AccessId == null) throw new ArgumentException($"{nameof(usageParams.AccessId)} should not be null", nameof(usageParams));
            var clientIdentity = usageParams.ClientIdentity;

            // decoding accessId
            _logger.LogInformation($"AddUsage for {clientIdentity}, SentTraffic: {usageParams.SentTrafficByteCount / 1000000} MB, ReceivedTraffic: {usageParams.ReceivedTrafficByteCount / 1000000} MB");

            // get current accessToken
            var tokenService = AccessTokenService.FromId(usageParams.ClientIdentity.TokenId);
            var accessToken = await tokenService.GetAccessToken();

            // set clientIp
            // add usage
            var accessUsage = await tokenService.AddAccessUsage(
                clientId: clientIdentity.ClientId,
                clientIp: clientIdentity.ClientIp.ToString(),
                clientVersion: clientIdentity.ClientVersion,
                userAgent: clientIdentity.UserAgent,
                sentTraffic: usageParams.SentTrafficByteCount,
                receivedTraffic: usageParams.ReceivedTrafficByteCount);

            // create access
            var access = new Access()
            {
                AccessId = usageParams.AccessId,
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
        public async Task<Access> GetAccess(AccessParams accessParams)
        {
            var clientIdentity = accessParams.ClientIdentity ?? throw new ArgumentException($"{nameof(AccessParams.ClientIdentity)} should not be null.", nameof(accessParams));
            _logger.LogInformation($"Getting Access. TokenId: {clientIdentity.TokenId}, ClientId: {clientIdentity.ClientId}");

            // update client
            var clientService = ClientService.FromId(clientIdentity.ClientId);
            await clientService.AddOrUpdate(clientIdentity.ClientVersion, clientIdentity.UserAgent);

            var accessTokenService = AccessTokenService.FromId(clientIdentity.TokenId);
            var accessToken = await accessTokenService.GetAccessToken();

            // check endPointGroup
            var endPointService = ServerEndPointService.FromId(accessParams.ServerEndPoint.ToString());
            var endPoint = await endPointService.Get();
            if (accessToken.endPointGroupId != endPoint.serverEndPointGroupId)
            {
                _logger.LogWarning($"Client does not have access to this endPointGroup! client: {clientIdentity}, endPoint: {accessParams.ServerEndPoint}");
                return new Access { StatusCode = AccessStatusCode.Error };
            }

            // create return
            using var md5 = MD5.Create();
            var accessIdString = accessToken.endPointGroupId == 0 ? $"{accessToken.accessTokenId},{clientIdentity.ClientId}" : accessToken.accessTokenId.ToString();
            var accessId = Convert.ToBase64String(md5.ComputeHash(Encoding.UTF8.GetBytes(accessIdString)));
            return await AddUsage(new UsageParams { AccessId = accessId, ClientIdentity = accessParams.ClientIdentity });
        }

        [HttpGet]
        [Authorize(AuthenticationSchemes = "auth", Roles = "Admin, VpnServer")]
        [Route(nameof(GetSslCertificateData))]
        public async Task<byte[]> GetSslCertificateData(string serverEndPoint)
        {
            var certificateService = ServerEndPointService.FromId(serverEndPoint);
            var res = await certificateService.Get();
            return res.certificateRawData;
        }

        public Task SendServerStatus(ServerStatus serverStatus)
        {
            return Task.FromResult(0);
        }
    }
}
