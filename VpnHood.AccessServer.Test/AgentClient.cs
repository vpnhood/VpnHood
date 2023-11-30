using VpnHood.Server.Access.Managers.Http;

namespace VpnHood.AccessServer.Test;

public class AgentClient : HttpAccessManager
{
    public AgentClient(HttpClient httpClient, HttpAccessManagerOptions options) 
        : base(httpClient, options)
    {
    }
}