using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using VpnHood.Common;
using VpnHood.Common.Messaging;
using VpnHood.Server.Exceptions;
using VpnHood.Server.Messaging;

namespace VpnHood.Server.AccessServers
{
    public class RestAccessServer : IAccessServer
    {
        private readonly string _authorization;
        private readonly HttpClient _httpClient;

        public RestAccessServer(Uri baseUri, string authorization)
        {
            //if (baseUri.Scheme != Uri.UriSchemeHttps)
            //  throw new ArgumentException("baseUri must be https!", nameof(baseUri));
            if (baseUri.ToString()[..1] != "/") baseUri = new Uri(baseUri.AbsoluteUri + "/");

            BaseUri = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
            _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            var handler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                ServerCertificateCustomValidationCallback = ServerCertificateCustomValidationCallback
            };
            _httpClient = new HttpClient(handler);
        }

        public string? RestCertificateThumbprint { get; set; }
        public Uri BaseUri { get; }

        public bool IsMaintenanceMode { get; private set; }

        public Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx)
        {
            return SendRequest<SessionResponseEx>("sessions", HttpMethod.Post, new { }, sessionRequestEx);
        }

        public Task<SessionResponseEx> Session_Get(uint sessionId, IPEndPoint hostEndPoint, IPAddress? clientIp)
        {
            return SendRequest<SessionResponseEx>($"sessions/{sessionId}", HttpMethod.Get,
                new {hostEndPoint, clientIp});
        }

        public Task<ResponseBase> Session_AddUsage(uint sessionId, bool closeSession, UsageInfo usageInfo)
        {
            return SendRequest<ResponseBase>($"sessions/{sessionId}/usage", HttpMethod.Post, new {closeSession},
                usageInfo);
        }

        public Task<byte[]> GetSslCertificateData(IPEndPoint hostEndPoint)
        {
            return SendRequest<byte[]>($"ssl-certificates/{hostEndPoint}", HttpMethod.Get, new { });
        }

        public Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus)
        {
            return SendRequest<ServerCommand>("server-status", HttpMethod.Post, bodyParams: serverStatus);
        }

        public Task<ServerConfig> Server_Configure(ServerInfo serverInfo)
        {
            return SendRequest<ServerConfig>("server-configure", HttpMethod.Post, bodyParams: serverInfo);
        }

        public void Dispose()
        {
        }

        private bool ServerCertificateCustomValidationCallback(HttpRequestMessage httpRequestMessage,
            X509Certificate2 x509Certificate2, X509Chain x509Chain, SslPolicyErrors sslPolicyErrors)
        {
            return sslPolicyErrors == SslPolicyErrors.None ||
                   x509Certificate2.Thumbprint!.Equals(RestCertificateThumbprint, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<T> SendRequest<T>(string api, HttpMethod httpMethod, object? queryParams = null,
            object? bodyParams = null)
        {
            var jsonSerializerOptions = new JsonSerializerOptions {PropertyNamingPolicy = JsonNamingPolicy.CamelCase};
            var ret = await SendRequest(api, httpMethod, queryParams, bodyParams);
            return JsonSerializer.Deserialize<T>(ret, jsonSerializerOptions) ??
                   throw new FormatException($"Invalid {typeof(T).Name}!");
        }

        private async Task<string> SendRequest(string api, HttpMethod httpMethod, object? queryParams = null,
            object? bodyParams = null)
        {
            var jsonSerializerOptions = new JsonSerializerOptions {PropertyNamingPolicy = JsonNamingPolicy.CamelCase};
            var uriBuilder = new UriBuilder(new Uri(BaseUri, api));
            var query = HttpUtility.ParseQueryString(string.Empty);

            // use query string
            if (queryParams != null)
                foreach (var prop in queryParams.GetType().GetProperties())
                {
                    var value = prop.GetValue(queryParams, null)?.ToString();
                    if (value != null)
                        query.Add(prop.Name, value);
                }

            uriBuilder.Query = query.ToString();

            // create request
            uriBuilder.Query = query.ToString();
            var requestMessage = new HttpRequestMessage(httpMethod, uriBuilder.Uri);
            requestMessage.Headers.Add("authorization", _authorization);
            if (bodyParams != null)
                requestMessage.Content = new StringContent(JsonSerializer.Serialize(bodyParams, jsonSerializerOptions),
                    Encoding.UTF8, "application/json");

            // send request
            try
            {
                // get connection to server
                var response = await _httpClient.SendAsync(requestMessage);
                await using var stream = await response.Content.ReadAsStreamAsync();
                var streamReader = new StreamReader(stream);
                var ret = await streamReader.ReadToEndAsync();

                // check maintenance mode
                IsMaintenanceMode = response.StatusCode == HttpStatusCode.ServiceUnavailable;
                if (IsMaintenanceMode)
                    throw new MaintenanceException(ret);

                // check status
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new Exception(
                        $"Invalid status code from RestAccessServer! Status: {response.StatusCode}, Message: {ret}");

                return ret;
            }
            catch (Exception ex) when (Util.IsConnectionRefusedException(ex))
            {
                IsMaintenanceMode = true;
                throw new MaintenanceException();
            }
        }
    }
}