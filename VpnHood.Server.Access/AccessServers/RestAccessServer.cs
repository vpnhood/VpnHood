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
using VpnHood.Server.Exceptions;

namespace VpnHood.Server.AccessServers
{
    public class RestAccessServer : IAccessServer
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string _authHeader;
        public string ValidCertificateThumbprint { get; set; }
        public Uri BaseUri { get; }
        public Guid ServerId { get; }

        public bool IsMaintenanceMode { get; private set; } = false;

        public RestAccessServer(Uri baseUri, string authHeader, Guid serverId)
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

        private async Task<T> SendRequest<T>(string api, HttpMethod httpMethod, object queryParams = null, object bodyParams = null)
        {
            var uriBuilder = new UriBuilder(new Uri(BaseUri, api));
            var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
            query.Add("serverId", ServerId.ToString());

            // use query string
            if (queryParams != null)
            {
                foreach (var prop in queryParams.GetType().GetProperties())
                {
                    var value = prop.GetValue(queryParams, null)?.ToString();
                    if (value != null)
                        query.Add(prop.Name, value);
                }
            }
            uriBuilder.Query = query.ToString();

            // create request
            uriBuilder.Query = query.ToString();
            var requestMessage = new HttpRequestMessage(httpMethod, uriBuilder.Uri);
            requestMessage.Headers.Add("authorization", _authHeader);
            if (bodyParams != null)
                requestMessage.Content = new StringContent(JsonSerializer.Serialize(bodyParams), Encoding.UTF8, "application/json");

            // send request
            try
            {
                // get connection to server
                var response = await _httpClient.SendAsync(requestMessage);
                using var stream = await response.Content.ReadAsStreamAsync();
                var streamReader = new StreamReader(stream);
                var ret = streamReader.ReadToEnd();

                // check maintenance mode
                IsMaintenanceMode = response.StatusCode == HttpStatusCode.ServiceUnavailable;
                if (IsMaintenanceMode)
                    throw new MaintenanceException(ret);

                // check status
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new Exception($"Invalid status code from RestAccessServer! Status: {response.StatusCode}, Message: {ret}");

                if (typeof(T) == typeof(string))
                    return (T)(object)ret; //todo check to use it for cmd project

                var jsonSerializerOptions = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<T>(ret, jsonSerializerOptions);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                IsMaintenanceMode = true;
                throw new MaintenanceException();
            }
        }

        public Task<Access> GetAccess(AccessRequest accessRequest) 
            => SendRequest<Access>("Access", httpMethod: HttpMethod.Get, bodyParams: accessRequest);

        public Task<Access> AddUsage(string accessId, UsageInfo addUsageInfo)
            => SendRequest<Access>("Access/Usage", httpMethod: HttpMethod.Post, queryParams: new { accessId }, bodyParams: addUsageInfo);

        public Task<byte[]> GetSslCertificateData(string serverEndPoint) 
            => SendRequest<byte[]>("SslCertificate", httpMethod: HttpMethod.Get, queryParams: new { serverEndPoint });

        public Task SendServerStatus(ServerStatus serverStatus)
            => SendRequest<byte[]>("ServerStatus", httpMethod: HttpMethod.Post, bodyParams: serverStatus);
        
        public Task SubscribeServer(ServerInfo serverInfo) 
            => SendRequest<byte[]>("server-subscribe", httpMethod: HttpMethod.Post, bodyParams: serverInfo);
    }
}