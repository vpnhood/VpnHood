using System;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace VpnHood.AccessServer.Auth
{
    public class AppAuthentication
    {
        private readonly AuthProviderItem[] _authProviderItems;
        private readonly RequestDelegate _next;
        private readonly ConcurrentDictionary<string, ClaimsPrincipal> _tokenCache = new();

        // remove expired tokens
        private DateTime _lastCleanUpTime = DateTime.MinValue;

        public AppAuthentication(RequestDelegate next, AuthProviderItem[] authProviderSettings)
        {
            _next = next;
            _authProviderItems = authProviderSettings;
        }

        private void CleanupCache()
        {
            if ((DateTime.UtcNow - _lastCleanUpTime).TotalMilliseconds > 15)
                return;
            _lastCleanUpTime = DateTime.UtcNow;

            foreach (var item in _tokenCache.ToArray())
            {
                var jwtExpValue = long.Parse(item.Value.Claims.First(x => x.Type == "exp").Value);
                var expirationTime = DateTimeOffset.FromUnixTimeSeconds(jwtExpValue).DateTime;
                if (DateTime.UtcNow > expirationTime)
                    _tokenCache.TryRemove(item.Key, out _);
            }
        }

        // ReSharper disable once UnusedMember.Global
        public async Task Invoke(HttpContext context)
        {
            //Remove Expired Tokens
            CleanupCache();

            var authHeader = context.Request.Headers["authorization"].ToString();
            if (authHeader is {Length: > 7} && authHeader[..7].Equals("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var tokenString = authHeader[7..];
                var token = new JwtSecurityTokenHandler().ReadToken(tokenString);

                // check in cache
                if (_tokenCache.TryGetValue(tokenString, out var principal))
                {
                    context.User = principal;
                }
                else
                {
                    // find authentication scheme
                    var authProviderSettings = _authProviderItems.FirstOrDefault(x => x.Issuers.Contains(token.Issuer));
                    if (authProviderSettings != null)
                    {
                        // create new ticket
                        var result = await context.AuthenticateAsync(authProviderSettings.Schema);
                        if (result.Failure != null)
                            throw result.Failure;
                        context.User = result.Principal ??
                                       throw new UnauthorizedAccessException("Token does not have Principal!");

                        _tokenCache.TryAdd(tokenString, result.Principal);
                    }
                }
            }

            await _next(context);
        }
    }
}