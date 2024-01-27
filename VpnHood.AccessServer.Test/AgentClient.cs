using VpnHood.Server.Access.Managers.Http;

namespace VpnHood.AccessServer.Test;

public class AgentClient(HttpClient httpClient, HttpAccessManagerOptions options)
    : HttpAccessManager(httpClient, options);