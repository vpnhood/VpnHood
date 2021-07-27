using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VpnHood.AccessServer.Models;
using VpnHood.Server;

//todo use nuget

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize(AuthenticationSchemes = "auth", Roles = "Admin, VpnServer")]
    public class AccessController : SuperController<AccessController>, IAccessServer
    {
        public AccessController(ILogger<AccessController> logger) : base(logger)
        {
        }

        [HttpPost]
        [Route(nameof(AddUsage))]
        public async Task<Access> AddUsage(UsageParams usageParams)
        {
            if (usageParams.ClientIdentity == null) throw new ArgumentException($"{nameof(usageParams.ClientIdentity)} should not be null", nameof(usageParams));
            if (usageParams.AccessId == null) throw new ArgumentException($"{nameof(usageParams.AccessId)} should not be null", nameof(usageParams));
            var clientIdentity = usageParams.ClientIdentity;
            _logger.LogInformation($"AddUsage for {clientIdentity}, SentTraffic: {usageParams.SentTrafficByteCount / 1000000} MB, ReceivedTraffic: {usageParams.ReceivedTrafficByteCount / 1000000} MB");

            // get accessToken
            using VhContext vhContext = new();
            var accessToken = await vhContext.AccessTokens.SingleAsync(e => e.AccessTokenId == clientIdentity.TokenId);
            var ret = await AddUsageInternal(vhContext, accessToken, usageParams, false);
            await vhContext.SaveChangesAsync();
            return ret;
        }

        private async Task<Access> AddUsageInternal(VhContext vhContext, AccessToken accessToken, UsageParams usageParams, bool isConnecting)
        {
            var clientIdentity = usageParams.ClientIdentity;

            // get current accessToken
            await PublicCycleHelper.UpdateCycle(vhContext);

            // add usage
            var clientId = accessToken.IsPublic ? clientIdentity.ClientId : Guid.Empty;
            var accessUsage = await vhContext.AccessUsages.FindAsync(clientIdentity.TokenId, clientId);
            if (accessUsage == null)
            {
                accessUsage = new AccessUsage
                {
                    AccessTokenId = clientIdentity.TokenId,
                    ClientId = clientIdentity.ClientId,
                    CycleReceivedTraffic = usageParams.ReceivedTrafficByteCount,
                    CycleSentTraffic = usageParams.SentTrafficByteCount,
                    TotalReceivedTraffic = usageParams.ReceivedTrafficByteCount,
                    TotalSentTraffic = usageParams.SentTrafficByteCount,
                    ConnectTime = DateTime.Now,
                    ModifiedTime = DateTime.Now
                };
                await vhContext.AccessUsages.AddAsync(accessUsage);
            }
            else
            {
                accessUsage.CycleReceivedTraffic += usageParams.ReceivedTrafficByteCount;
                accessUsage.CycleSentTraffic += usageParams.SentTrafficByteCount;
                accessUsage.TotalReceivedTraffic += usageParams.ReceivedTrafficByteCount;
                accessUsage.TotalSentTraffic += usageParams.SentTrafficByteCount;
                accessUsage.ModifiedTime = DateTime.Now;
                if (isConnecting) accessUsage.ConnectTime = DateTime.Now;
                vhContext.AccessUsages.Update(accessUsage);
            }
            await vhContext.SaveChangesAsync();

            // create access
            var access = new Access()
            {
                AccessId = usageParams.AccessId,
                Secret = accessToken.Secret,
                ExpirationTime = accessToken.EndTime,
                MaxClientCount = accessToken.MaxClient,
                MaxTrafficByteCount = accessToken.MaxTraffic,
                ReceivedTrafficByteCount = accessUsage.CycleReceivedTraffic,
                SentTrafficByteCount = accessUsage.CycleSentTraffic,
            };

            // set expiration time on first use
            if (access.ExpirationTime == null && access.SentTrafficByteCount != 0 && access.ReceivedTrafficByteCount != 0 && accessToken.Lifetime != 0)
            {
                access.ExpirationTime = DateTime.Now.AddDays(accessToken.Lifetime);
                _logger.LogInformation($"Access has been activated! Expiration: {access.ExpirationTime}, ClientIdentity: {clientIdentity}");
            }

            // calculate status
            if (access.ExpirationTime < DateTime.Now)
                access.StatusCode = AccessStatusCode.Expired;
            else if (accessToken.MaxTraffic != 0 && access.SentTrafficByteCount + access.ReceivedTrafficByteCount > accessToken.MaxTraffic)
                access.StatusCode = AccessStatusCode.TrafficOverflow;
            else
                access.StatusCode = AccessStatusCode.Ok;

            return access;
        }

        [HttpGet]
        [Route(nameof(GetAccess))]
        public async Task<Access> GetAccess(AccessParams accessParams)
        {
            var clientIdentity = accessParams.ClientIdentity ?? throw new ArgumentException($"{nameof(AccessParams.ClientIdentity)} should not be null.", nameof(accessParams));
            _logger.LogInformation($"Getting Access. TokenId: {clientIdentity.TokenId}, ClientId: {clientIdentity.ClientId}");

            using VhContext vhContext = new();

            // Check AccountId
            // check accessToken, accessTokenGroup
            var query = from AC in vhContext.Accounts
                        join ATG in vhContext.AccessTokenGroups on AC.AccountId equals ATG.AccountId
                        join AT in vhContext.AccessTokens on ATG.AccessTokenGroupId equals AT.AccessTokenGroupId
                        join EP in vhContext.ServerEndPoints on ATG.AccessTokenGroupId equals EP.AccessTokenGroupId
                        where AC.AccountId == AccountId && AT.AccessTokenId == clientIdentity.TokenId &&
                                (EP.PulicEndPoint == accessParams.RequestEndPoint.ToString() || EP.LocalEndPoint == accessParams.RequestEndPoint.ToString())
                        select new { AT };
            var result = await query.SingleAsync();
            var accessToken = result.AT;

            // update client
            var client = await vhContext.Clients.FindAsync(clientIdentity.ClientId);
            if (client == null)
            {
                client = new Client
                {
                    ClientId = clientIdentity.ClientId,
                    ClientVersion = clientIdentity.ClientVersion,
                    UserAgent = clientIdentity.UserAgent,
                    CreatedTime = DateTime.Now,
                };
                await vhContext.Clients.AddAsync(client);
            }
            else
            {
                client.UserAgent = clientIdentity.UserAgent;
                client.ClientVersion = clientIdentity.ClientVersion;
                vhContext.Clients.Update(client);
            }

            // create return
            using var md5 = MD5.Create();
            var accessIdString = accessToken.IsPublic ? $"{accessToken.AccessTokenId},{clientIdentity.ClientId}" : accessToken.AccessTokenId.ToString();
            var accessId = Convert.ToBase64String(md5.ComputeHash(Encoding.UTF8.GetBytes(accessIdString)));
            var ret = await AddUsageInternal(vhContext, accessToken, new UsageParams { AccessId = accessId, ClientIdentity = accessParams.ClientIdentity }, true);
            await vhContext.SaveChangesAsync();
            return ret;
        }

        [HttpGet]
        [Route(nameof(GetSslCertificateData))]
        public async Task<byte[]> GetSslCertificateData(string serverEndPoint)
        {
            using VhContext vhContext = new();
            var serEndPoint = await vhContext.ServerEndPoints.SingleAsync(x => x.PulicEndPoint == serverEndPoint);
            return serEndPoint.CertificateRawData;
        }

        [HttpPost]
        [Route(nameof(SendServerStatus))]
        public Task SendServerStatus(ServerStatus serverStatus)
        {
            return Task.FromResult(0);
        }
    }
}
