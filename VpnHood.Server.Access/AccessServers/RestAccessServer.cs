using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VpnHood.Server.AccessServers
{
    public class RestAccessServer : IAccessServer
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string _authHeader;
        public string ValidCertificateThumbprint { get; set; }
        public Uri BaseUri { get; }
        public string ServerId { get; }

        public RestAccessServer(Uri baseUri, string authHeader, string serverId)
        {
            //if (baseUri.Scheme != Uri.UriSchemeHttps)
            //  throw new ArgumentException("baseUri must be https!", nameof(baseUri));

            BaseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
            _authHeader = authHeader ?? throw new ArgumentNullException(nameof(authHeader));
            ServerId = serverId;
            var handler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                ServerCertificateCustomValidationCallback = ServerCertificateCustomValidationCallback
            };
            _httpClient = new HttpClient(handler);
        }

        private bool ServerCertificateCustomValidationCallback(HttpRequestMessage httpRequestMessage, X509Certificate2 x509Certificate2, X509Chain x509Chain, SslPolicyErrors sslPolicyErrors)
        {
            return sslPolicyErrors == SslPolicyErrors.None || x509Certificate2.Thumbprint.Equals(ValidCertificateThumbprint, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<T> SendRequest<T>(string api, object paramerters, HttpMethod httpMethod, bool useBody)
        {
            var uriBuilder = new UriBuilder(new Uri(BaseUri, api));
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);

            // use query string
            if (!useBody)
            {
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
            uriBuilder.Query = query.ToString();
            var requestMessage = new HttpRequestMessage(httpMethod, uriBuilder.Uri);
            requestMessage.Headers.Add("authorization", _authHeader);
            requestMessage.Headers.Add("serverId", ServerId);
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

        public Task<Access> GetAccess(AccessParams accessParams) =>
            SendRequest<Access>(nameof(GetAccess), accessParams, HttpMethod.Get, true);

        public Task<Access> AddUsage(UsageParams addUsageParams) =>
            SendRequest<Access>(nameof(AddUsage), addUsageParams, HttpMethod.Post, true);

        public Task<byte[]> GetSslCertificateData(string serverEndPoint) =>
            SendRequest<byte[]>(nameof(GetSslCertificateData), new { serverEndPoint }, HttpMethod.Get, false);

        public Task SendServerStatus(ServerStatus serverStatus) =>
            SendRequest<byte[]>(nameof(SendServerStatus), serverStatus, HttpMethod.Post, false);
    }
}