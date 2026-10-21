using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.Test.Providers;

// an API client with one call a test can make
public class TestApiClient(HttpClient httpClient) : ApiClientBase(httpClient)
{
    public Task<string> Get(CancellationToken cancellationToken)
    {
        return HttpGetAsync<string>("test", cancellationToken: cancellationToken);
    }
}
