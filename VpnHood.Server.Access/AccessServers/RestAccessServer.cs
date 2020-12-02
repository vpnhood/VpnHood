using System;
using System.IO;
using System.Net;
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
            //if (baseUri.Scheme != Uri.UriSchemeHttps)
            //  throw new ArgumentException("baseUri must be https!", nameof(baseUri));

            BaseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
            _authHeader = authHeader ?? throw new ArgumentNullException(nameof(authHeader));
        }

        private async Task<T> SendRequest<T>(string api, object paramerters, HttpMethod httpMethod, bool useBody)
        {
            var uriBuilder = new UriBuilder(new Uri(BaseUri, api));

            // use query string
            if (!useBody)
            {
                var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
                var type = paramerters.GetType();
                foreach (var prop in type.GetProperties())
                {
                    var value = prop.GetValue(paramerters, null)?.ToString();
                    if (value != null)
                        query.Add(prop.Name, value);
                }
                uriBuilder.Query = query.ToString();
            }

            // create request
            var uri = uriBuilder.ToString();
            var requestMessage = new HttpRequestMessage(httpMethod, uri);
            requestMessage.Headers.Add("authorization", _authHeader);
            if (useBody)
                requestMessage.Content = new StringContent(JsonSerializer.Serialize(paramerters), Encoding.UTF8, "application/json");

            // send request
            var res = await _httpClient.SendAsync(requestMessage);
            using var stream = await res.Content.ReadAsStreamAsync();
            var streamReader = new StreamReader(stream);
            var ret = streamReader.ReadToEnd();

            if (res.StatusCode != HttpStatusCode.OK)
                throw new Exception($"Invalid status code from RestAccessServer! Status: {res.StatusCode}, Message: {ret}");

            var jsonSerializerOptions = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<T>(ret, jsonSerializerOptions);
        }

        public Task<Access> GetAccess(ClientIdentity clientIdentity) =>
            SendRequest<Access>(nameof(GetAccess), clientIdentity, HttpMethod.Get, true);

        public Task<Access> AddUsage(AddUsageParams addUsageParams) =>
            SendRequest<Access>(nameof(AddUsage), addUsageParams, HttpMethod.Post, true);

        public Task<byte[]> GetSslCertificateData(string serverEndPoint) =>
            SendRequest<byte[]>(nameof(GetSslCertificateData), new { serverEndPoint }, HttpMethod.Get, false);
    }
}
