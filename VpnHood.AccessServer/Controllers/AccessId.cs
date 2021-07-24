using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using VpnHood.AccessServer.Models;
using VpnHood.Logging;
using VpnHood.Server;

namespace VpnHood.AccessServer.Controllers
{
    internal class AccessId
    {
        public Guid TokenId { get; set; }
        public Guid ClientId { get; set; }

        public override string ToString() => JsonSerializer.Serialize(this);
        public static AccessId Parse(string value)
        {
            var ret = JsonSerializer.Deserialize<AccessId>(value);
            if (ret.ClientId == null || ret.TokenId == Guid.Empty)
                throw new FormatException($"{nameof(AccessId)} has invalid format");
            return ret;
        }

        public static Access CreateAccess(AccessId accessId, AccessToken accessToken, AccessUsage accessUsage)
        {
            var access = new Access()
            {
                AccessId = accessId.ToString(),
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
                VhLogger.Instance.LogInformation($"Access has been activated! Expiration: {access.ExpirationTime}, AccessId: {accessId}");
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

    }
}
