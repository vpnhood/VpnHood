using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;
using VpnHood.Common.ApiClients;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Server.Access.Configurations;
using VpnHood.Server.Access.Messaging;

namespace VpnHood.Server.Access.Managers.HttpAccessManagers;

public class HttpAccessManager : ApiClientBase, IAccessManager
{
    public bool IsMaintenanceMode { get; private set; }

    public HttpAccessManager(HttpClient httpClient)
        : base(httpClient)
    {
    }

    public HttpAccessManager(HttpAccessManagerOptions options)
        : this(new HttpClient(), options)
    {
    }

    public HttpAccessManager(HttpClient httpClient, HttpAccessManagerOptions options)
        : base(httpClient)
    {
        DefaultBaseAddress =
            new UriBuilder(options.BaseUrl.Scheme, options.BaseUrl.Host, options.BaseUrl.Port, "api/agent/").Uri;

        if (AuthenticationHeaderValue.TryParse(options.Authorization, out var authenticationHeaderValue))
            DefaultAuthorization = authenticationHeaderValue;
    }

    protected override Task ProcessResponseAsync(HttpClient client, HttpResponseMessage response, CancellationToken ct)
    {
        // check maintenance mode
        IsMaintenanceMode = response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.Forbidden;
        if (IsMaintenanceMode)
            throw new MaintenanceException();

        return base.ProcessResponseAsync(client, response, ct);
    }

    protected override async Task<HttpResult<T>> HttpSendAsync<T>(string urlPart,
        Dictionary<string, object?>? parameters, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try {
            return await base.HttpSendAsync<T>(urlPart, parameters, request, cancellationToken).VhConfigureAwait();
        }
        catch (Exception ex) when (VhUtil.IsConnectionRefusedException(ex)) {
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

    public Task<SessionResponseEx> Session_Get(ulong sessionId, IPEndPoint hostEndPoint, IPAddress? clientIp)
    {
        var parameters = new Dictionary<string, object?> {
            { "sessionId", sessionId },
            { "hostEndPoint", hostEndPoint },
            { "clientIp", clientIp }
        };

        return HttpGetAsync<SessionResponseEx>($"sessions/{sessionId}", parameters);
    }

    public Task<SessionResponseEx[]> Session_GetAll()
    {
        return HttpGetAsync<SessionResponseEx[]>("sessions");
    }

    public Task<SessionResponse> Session_AddUsage(ulong sessionId, Traffic traffic, string? adData)
    {
        var parameters = new Dictionary<string, object?> {
            { "sessionId", sessionId },
            { "adData", adData },
            { "closeSession", false }
        };

        return HttpPostAsync<SessionResponse>($"sessions/{sessionId}/usage", parameters, traffic);
    }

    public Task<Dictionary<ulong, SessionResponse>> Session_AddUsages(SessionUsage[] sessionUsages)
    {
        return HttpPostAsync<Dictionary<ulong, SessionResponse>>("sessions/usages", null, data: sessionUsages);
    }

    public Task<SessionResponse> Session_Close(ulong sessionId, Traffic traffic)
    {
        var parameters = new Dictionary<string, object?> {
            { "sessionId", sessionId },
            { "closeSession", true }
        };

        return HttpPostAsync<SessionResponse>($"sessions/{sessionId}/usage", parameters, traffic);
    }

    public Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus)
    {
        return HttpPostAsync<ServerCommand>("status", null, serverStatus);
    }

    public Task<ServerConfig> Server_Configure(ServerInfo serverInfo)
    {
        return HttpPostAsync<ServerConfig>("configure", null, serverInfo);
    }

    public virtual void Dispose()
    {
    }
}