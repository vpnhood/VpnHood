using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Server.Access.Managers.HttpAccessManagers;

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

    protected override Task ProcessResponseAsync(HttpClient client, HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        // check maintenance mode
        IsMaintenanceMode = response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.Forbidden;
        if (IsMaintenanceMode)
            throw new MaintenanceException();

        return base.ProcessResponseAsync(client, response, cancellationToken);
    }

    protected override async Task<HttpResult<T>> HttpSendAsync<T>(string urlPart,
        Dictionary<string, object?>? parameters, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try {
            return await base.HttpSendAsync<T>(urlPart, parameters, request, cancellationToken).Vhc();
        }
        catch (Exception ex) when (VhUtils.IsConnectionRefusedException(ex)) {
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

    public Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx, CancellationToken cancellationToken)
    {
        return HttpPostAsync<SessionResponseEx>("sessions", null, sessionRequestEx, cancellationToken);
    }

    public Task<SessionResponseEx> Session_Get(ulong sessionId, IPEndPoint hostEndPoint, IPAddress? clientIp,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object?> {
            { "sessionId", sessionId },
            { "hostEndPoint", hostEndPoint },
            { "clientIp", clientIp }
        };

        return HttpGetAsync<SessionResponseEx>($"sessions/{sessionId}", parameters, cancellationToken);
    }

    public Task<SessionResponseEx[]> Session_GetAll(CancellationToken cancellationToken)
    {
        return HttpGetAsync<SessionResponseEx[]>("sessions", cancellationToken: cancellationToken);
    }

    public Task<SessionResponse> Session_AddUsage(ulong sessionId, Traffic traffic, string? adData,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object?> {
            { "sessionId", sessionId },
            { "adData", adData },
            { "closeSession", false }
        };

        return HttpPostAsync<SessionResponse>($"sessions/{sessionId}/usage", parameters, traffic, cancellationToken);
    }

    public Task<Dictionary<ulong, SessionResponse>> Session_AddUsages(SessionUsage[] sessionUsages,
        CancellationToken cancellationToken)
    {
        return HttpPostAsync<Dictionary<ulong, SessionResponse>>("sessions/usages", null, data: sessionUsages,
            cancellationToken);
    }

    public Task<SessionResponse> Session_Close(ulong sessionId, Traffic traffic, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object?> {
            { "sessionId", sessionId },
            { "closeSession", true }
        };

        return HttpPostAsync<SessionResponse>($"sessions/{sessionId}/usage", parameters, traffic, cancellationToken);
    }

    public Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus, CancellationToken cancellationToken)
    {
        return HttpPostAsync<ServerCommand>("status", null, serverStatus, cancellationToken);
    }

    public Task<ServerConfig> Server_Configure(ServerInfo serverInfo, CancellationToken cancellationToken)
    {
        return HttpPostAsync<ServerConfig>("configure", null, serverInfo, cancellationToken);
    }

    public async Task<string> Acme_GetHttp01KeyAuthorization(string token, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object?> {
            { "token", token }
        };

        try {
            return await HttpGetAsync<string>("acme/http01_key_authorization", parameters, cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound) {
            throw new KeyNotFoundException("The requested token was not found.", ex);
        }
    }

    public virtual void Dispose()
    {
    }
}