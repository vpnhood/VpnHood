using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VpnHood.AccessServer.Apis
{
    public class ApiBase
    {
        public static Uri BaseAddress { get; set; }
        public static string Authorization { get; set; }
        protected static Task PrepareRequestAsync(HttpClient client, HttpRequestMessage request, StringBuilder urlBuilder)
        {
            _ = request;
            _ = urlBuilder;
            client.BaseAddress = BaseAddress;
            client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(Authorization);
            return Task.FromResult(0);
        }

        protected static Task PrepareRequestAsync(HttpClient client, HttpRequestMessage request, string url)
        {
            _ = request;
            _ = url;
            client.BaseAddress = BaseAddress;
            client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(Authorization);
            return Task.FromResult(0);
        }

        protected static Task ProcessResponseAsync(HttpClient client, HttpResponseMessage response, CancellationToken cancellationToken)
        {
            _ = client;
            _ = response;
            _ = cancellationToken;
            return Task.FromResult(0);
        }
    }
}
