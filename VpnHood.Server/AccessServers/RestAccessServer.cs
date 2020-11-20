using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VpnHood.Loggers;

namespace VpnHood.Server.AccessServers
{
    public class RestAccessServer : IAccessServer
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string _authHeader;
        public Uri BaseUri { get; }

        public RestAccessServer(Uri baseUri, string authHeader)
        {
            //if (baseUri.Scheme != Uri.UriSchemeHttps)
            //  throw new ArgumentException("baseUri must be https!", nameof(baseUri));

            BaseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
            _authHeader = authHeader ?? throw new ArgumentNullException(nameof(authHeader));
        }

        public async Task<Access> GetAccess(ClientIdentity clientIdentity)
        {
            try
            {
                var uri = new Uri(BaseUri, "getaccess");
                var requestMessage = new HttpRequestMessage(HttpMethod.Get, uri);
                requestMessage.Headers.Add("authorization", _authHeader);

                // send request
                var serializerOptions = new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var json = JsonSerializer.Serialize(clientIdentity, serializerOptions);
                requestMessage.Content = new StringContent(json, Encoding.UTF8, "application/json");
                var res = await _httpClient.SendAsync(requestMessage);
                if (res.StatusCode != System.Net.HttpStatusCode.OK)
                    throw new Exception($"Invalid status code from RestAccessServer! Status: {res.StatusCode}, Message: {await res.Content.ReadAsStringAsync()}");

                // return result
                var jsonAccess = await res.Content.ReadAsStringAsync();
                var access = JsonSerializer.Deserialize<Access>(jsonAccess, serializerOptions);
                return access;

            }
            catch (Exception ex)
            {
                Logger.Current.LogError(ex.Message);
                throw;
            }
        }

        public async Task<Access> AddUsage(AddUsageParams addUsageParams)
        {
            try
            {
                var uri = new Uri(BaseUri, "addusage");
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, uri);
                requestMessage.Headers.Add("authorization", _authHeader);

                // send request
                var serializerOptions = new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var json = JsonSerializer.Serialize(addUsageParams, serializerOptions);
                requestMessage.Content = new StringContent(json, Encoding.UTF8, "application/json");
                var res = await _httpClient.SendAsync(requestMessage);

                // return result
                var jsonAccess = await res.Content.ReadAsStringAsync();
                var access = JsonSerializer.Deserialize<Access>(jsonAccess, serializerOptions);
                return access;
            }
            catch (Exception ex)
            {
                Logger.Current.LogError(ex.Message);
                throw;
            }
        }

        public Task<byte[]> GetSslCertificateData(string serverEndPoint)
        {
            throw new NotImplementedException();
        }
    }
}
