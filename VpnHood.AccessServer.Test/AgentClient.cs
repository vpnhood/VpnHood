using System.Net.Http;
using VpnHood.Server.Providers.HttpAccessServerProvider;

namespace VpnHood.AccessServer.Test;

public class AgentClient : HttpAccessServer
{
    public AgentClient(HttpClient httpClient, HttpAccessServerOptions options) 
        : base(httpClient, options)
    {
    }
}