using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace VpnHood.Server.ClientStores
{
    public class RestClientStore : IClientStore
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string _secret;
        private Uri BaseUri { get; }

        public RestClientStore(Uri baseUri, string secret)
        {
            if (baseUri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("baseUri muse be https!", nameof(baseUri));

            BaseUri = baseUri;
            _secret = secret;
        }

        public Task<ClientInfo> GetClientInfo(ClientIdentity clientIdentity, bool withToken)
        {
            throw new NotImplementedException();
        }

        public Task<ClientInfo> AddClientUsage(ClientIdentity clientIdentity, ClientUsage clientUsage, bool withToken)
        {
            throw new NotImplementedException();
        }
    }
}
