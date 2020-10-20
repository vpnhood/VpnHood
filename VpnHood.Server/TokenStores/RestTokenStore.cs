using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace VpnHood.Server.TokenStores
{
    public class RestTokenStore : ITokenStore
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string _secret;
        private Uri BaseUri { get; }

        public RestTokenStore(Uri baseUri, string secret)
        {
            if (baseUri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("baseUri muse be https!", nameof(baseUri));

            BaseUri = baseUri;
            _secret = secret;
        }


        public Task<TokenUsage> AddTokenUsage(Guid tokenId, long sentByteCount, long recievedByteCount)
        {
            throw new NotImplementedException();
        }

        public Task<TokenUsage> GetTokenUsage(Guid tokenId)
        {
            throw new NotImplementedException();
        }

        public Task<TokenInfo> GetTokenInfo(Guid tokenId)
        {
            var uri = new Uri(BaseUri, $"tokeninfo?tokenId={tokenId}&_secret={_secret}");
            _httpClient.GetStringAsync(uri);
            throw new NotImplementedException();
        }

    }
}
