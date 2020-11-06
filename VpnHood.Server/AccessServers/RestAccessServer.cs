using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VpnHood.Server.AccessServers
{
    public class RestAccessServer : IAccessServer
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string _authHeader;
        public Uri BaseUri { get; }

        public RestAccessServer(Uri baseUri, string authHeader)
        {
            if (baseUri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("baseUri must be https!", nameof(baseUri));

            BaseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
            _authHeader = authHeader ?? throw new ArgumentNullException(nameof(authHeader));
        }

        public async Task<Access> GetAccess(ClientIdentity clientIdentity)
        {
            var uri = new Uri(BaseUri, "getaccess");
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, uri);
            requestMessage.Headers.Add("authorization", _authHeader);
            requestMessage.Headers.Add("Content-Type", "application/json; charset=UTF-8");

            // send request
            var serializerOptions = new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(clientIdentity, serializerOptions);
            requestMessage.Content = new StringContent(json, Encoding.UTF8);
            var res = await _httpClient.SendAsync(requestMessage);

            // return result
            var jsonAccess = await res.Content.ReadAsStringAsync();
            var access = JsonSerializer.Deserialize<Access>(jsonAccess, serializerOptions);
            return access;
        }

        public async Task<Access> AddUsage(AddUsageParams addUsageParams)
        {
            var uri = new Uri(BaseUri, "addusage");
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, uri);
            requestMessage.Headers.Add("authorization", _authHeader);
            requestMessage.Headers.Add("Content-Type", "application/json; charset=UTF-8");

            // send request
            var serializerOptions = new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var json = JsonSerializer.Serialize(addUsageParams, serializerOptions);
            requestMessage.Content = new StringContent(json, Encoding.UTF8);
            var res = await _httpClient.SendAsync(requestMessage);

            // return result
            var jsonAccess = await res.Content.ReadAsStringAsync();
            var access = JsonSerializer.Deserialize<Access>(jsonAccess, serializerOptions);
            return access;
        }
    }
}
