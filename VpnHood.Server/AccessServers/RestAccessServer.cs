using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace VpnHood.Server.AccessServers
{
    //todo
    #pragma warning disable IDE0052 // Remove unread private members

    public class RestAccessServer : IAccessServer
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string _authHeader;
        private Uri BaseUri { get; }

        public RestAccessServer(Uri baseUri, string secret)
        {
            if (baseUri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("baseUri must be https!", nameof(baseUri));

            BaseUri = baseUri;
            _authHeader = secret;
        }

        public Task<Access> GetAccess(ClientIdentity clientIdentity)
        {
            return AddUsage(clientIdentity, 0, 0);
        }

        public Task<Access> AddUsage(ClientIdentity clientIdentity, long sentTraffic, long receivedTraffic)
        {
            throw new NotImplementedException();
        }
    }
}
