using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Common;
using VpnHood.Common.Client;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Messaging;
using VpnHood.Server.Messaging;

namespace VpnHood.Server.AccessServers;

public class RestAccessServer2 : ApiClientBase, IAccessServer
{
    public bool IsMaintenanceMode { get; private set; }

    public RestAccessServer2(HttpClient httpClient) 
        : base(httpClient)
    {
    }

    public static RestAccessServer2 Create(RestAccessServerOptions options)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new UriBuilder(options.BaseUrl.Scheme, options.BaseUrl.Host, options.BaseUrl.Port, "api/agent/").Uri
        };

        if (AuthenticationHeaderValue.TryParse(options.Authorization, out var authenticationHeaderValue))
            httpClient.DefaultRequestHeaders.Authorization = authenticationHeaderValue;

        var restAccessServer = new RestAccessServer2(httpClient);
        return restAccessServer;
    }

    protected override ValueTask ProcessResponseAsync(HttpClient client, HttpResponseMessage response, CancellationToken ct)
    {
        // check maintenance mode
        IsMaintenanceMode = response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.Forbidden;
        if (IsMaintenanceMode)
            throw new MaintenanceException();

        return new ValueTask();
    }

    protected override async Task<HttpResult<T>> HttpSendAsync<T>(string urlPart, Dictionary<string, object?>? parameters, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await base.HttpSendAsync<T>(urlPart, parameters, request, cancellationToken);
        }
        catch (Exception ex) when (Util.IsConnectionRefusedException(ex))
        {
            IsMaintenanceMode = true;
            throw new MaintenanceException();
        }
    }

    protected override JsonSerializerOptions CreateSerializerSettings()
    {
        var serializerSettings = base.CreateSerializerSettings();
        serializerSettings.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        return serializerSettings;
    }

    public Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx)
    {
        return HttpPostAsync<SessionResponseEx>("sessions", null, sessionRequestEx);
    }

    public Task<SessionResponseEx> Session_Get(uint sessionId, IPEndPoint hostEndPoint, IPAddress? clientIp)
    {
        var parameters = new Dictionary<string, object?>
        {
            { "sessionId",  sessionId},
            { "hostEndPoint", hostEndPoint},
            { "clientIp",  clientIp}
        };

        return HttpGetAsync<SessionResponseEx>($"sessions/{sessionId}", parameters);
    }

    public Task<ResponseBase> Session_AddUsage(uint sessionId, UsageInfo usageInfo)
    {
        var parameters = new Dictionary<string, object?>
        {
            { "sessionId",  sessionId},
            { "closeSession",  false}
        };

        return HttpPostAsync<ResponseBase>($"sessions/{sessionId}/usage", parameters, usageInfo);
    }

    public Task<ResponseBase> Session_Close(uint sessionId, UsageInfo usageInfo)
    {
        var parameters = new Dictionary<string, object?>
        {
            { "sessionId",  sessionId},
            { "closeSession",  true}
        };

        return HttpPostAsync<ResponseBase>($"sessions/{sessionId}/usage", parameters, usageInfo);
    }


    public Task<byte[]> GetSslCertificateData(IPEndPoint hostEndPoint)
    {
        return HttpGetAsync<byte[]>($"certificates/{hostEndPoint}");
    }

    public Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus)
    {
        return HttpPostAsync<ServerCommand>("status", null, serverStatus);
    }

    public Task<ServerConfig> Server_Configure(ServerInfo serverInfo)
    {
        return HttpPostAsync<ServerConfig>("configure", null, serverInfo);
    }

    public void Dispose()
    {
    }
}