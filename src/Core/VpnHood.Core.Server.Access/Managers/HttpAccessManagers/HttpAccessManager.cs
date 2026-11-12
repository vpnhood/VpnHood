using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Messaging;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Server.Access.Managers.HttpAccessManagers;

/// <summary>
/// Represents an access manager reached over HTTP, at its agent API under <c>api/agent/</c>.
/// </summary>
/// <remarks>
/// JSON goes in camel case. The agent's 503, a gateway's 502 or 504, Cloudflare's 520 to 524, and a refused connection
/// are taken as maintenance, and fail a call with <see cref="MaintenanceException"/>; 500 is the agent's own error.
/// </remarks>
public class HttpAccessManager : ApiClientBase, IAccessManager
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HttpAccessManager"/> class over an HTTP client, whose own base
    /// address and headers serve.
    /// </summary>
    /// <param name="httpClient">The HTTP client, which stays the caller's.</param>
    public HttpAccessManager(HttpClient httpClient)
        : base(httpClient)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpAccessManager"/> class with an HTTP client of its own.
    /// </summary>
    /// <param name="options">The access manager's address and authorization.</param>
    public HttpAccessManager(HttpAccessManagerOptions options)
        : this(new HttpClient(), options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpAccessManager"/> class over an HTTP client, at the options'
    /// address under <c>api/agent/</c>, with their authorization where it parses.
    /// </summary>
    /// <param name="httpClient">The HTTP client, which stays the caller's.</param>
    /// <param name="options">The access manager's address and authorization.</param>
    public HttpAccessManager(HttpClient httpClient, HttpAccessManagerOptions options)
        : base(httpClient)
    {
        DefaultBaseAddress =
            new UriBuilder(options.BaseUrl.Scheme, options.BaseUrl.Host, options.BaseUrl.Port, "api/agent/").Uri;

        if (AuthenticationHeaderValue.TryParse(options.Authorization, out var authenticationHeaderValue))
            DefaultAuthorization = authenticationHeaderValue;
    }

    /// <inheritdoc/>
    /// <exception cref="MaintenanceException">The answer's status says the access manager is under maintenance.</exception>
    protected override Task ProcessResponseAsync(HttpClient client, HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (IsMaintenanceStatusCode(response.StatusCode))
            throw new MaintenanceException();

        return base.ProcessResponseAsync(client, response, cancellationToken);
    }

    private static bool IsMaintenanceStatusCode(HttpStatusCode statusCode)
    {
        return (int)statusCode is 502 or 503 or 504 or (>= 520 and <= 524);
    }

    /// <inheritdoc/>
    /// <exception cref="MaintenanceException">
    /// The access manager refused the connection, or its answer's status says it is under maintenance.
    /// </exception>
    protected override async Task<HttpResult<T>> HttpSendAsync<T>(string urlPart,
        Dictionary<string, object?>? parameters, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try {
            return await base.HttpSendAsync<T>(urlPart, parameters, request, cancellationToken).Vhc();
        }
        catch (Exception ex) when (VhUtils.IsConnectionRefusedException(ex)) {
            throw new MaintenanceException();
        }
    }

    /// <inheritdoc/>
    /// <returns>The JSON settings, with names in camel case.</returns>
    protected override JsonSerializerOptions CreateSerializerSettings()
    {
        var serializerSettings = base.CreateSerializerSettings();
        serializerSettings.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        return serializerSettings;
    }

    /// <inheritdoc/>
    /// <exception cref="MaintenanceException">The access manager is under maintenance, or refused the connection.</exception>
    /// <exception cref="ApiException">
    /// The access manager answered with another error, or with an answer that could not be read, or that read as null
    /// where a body was expected.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or the HTTP client's own timeout passed.
    /// </exception>
    public Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx, CancellationToken cancellationToken)
    {
        return HttpPostAsync<SessionResponseEx>("sessions", null, sessionRequestEx, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="MaintenanceException">The access manager is under maintenance, or refused the connection.</exception>
    /// <exception cref="ApiException">
    /// The access manager answered with another error, or with an answer that could not be read, or that read as null
    /// where a body was expected.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or the HTTP client's own timeout passed.
    /// </exception>
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

    /// <inheritdoc/>
    /// <exception cref="MaintenanceException">The access manager is under maintenance, or refused the connection.</exception>
    /// <exception cref="ApiException">
    /// The access manager answered with another error, or with an answer that could not be read, or that read as null
    /// where a body was expected.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or the HTTP client's own timeout passed.
    /// </exception>
    public Task<SessionResponseEx[]> Session_GetAll(CancellationToken cancellationToken)
    {
        return HttpGetAsync<SessionResponseEx[]>("sessions", parameters: null, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="MaintenanceException">The access manager is under maintenance, or refused the connection.</exception>
    /// <exception cref="ApiException">
    /// The access manager answered with another error, or with an answer that could not be read, or that read as null
    /// where a body was expected.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or the HTTP client's own timeout passed.
    /// </exception>
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

    /// <inheritdoc/>
    /// <exception cref="MaintenanceException">The access manager is under maintenance, or refused the connection.</exception>
    /// <exception cref="ApiException">
    /// The access manager answered with another error, or with an answer that could not be read, or that read as null
    /// where a body was expected.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or the HTTP client's own timeout passed.
    /// </exception>
    public Task<Dictionary<ulong, SessionResponse>> Session_AddUsages(SessionUsage[] sessionUsages,
        CancellationToken cancellationToken)
    {
        return HttpPostAsync<Dictionary<ulong, SessionResponse>>("sessions/usages", null, data: sessionUsages,
            cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="MaintenanceException">The access manager is under maintenance, or refused the connection.</exception>
    /// <exception cref="ApiException">
    /// The access manager answered with another error, or with an answer that could not be read, or that read as null
    /// where a body was expected.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or the HTTP client's own timeout passed.
    /// </exception>
    public Task<SessionResponse> Session_Close(ulong sessionId, Traffic traffic, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object?> {
            { "sessionId", sessionId },
            { "closeSession", true }
        };

        return HttpPostAsync<SessionResponse>($"sessions/{sessionId}/usage", parameters, traffic, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="MaintenanceException">The access manager is under maintenance, or refused the connection.</exception>
    /// <exception cref="ApiException">
    /// The access manager answered with another error, or with an answer that could not be read, or that read as null
    /// where a body was expected.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or the HTTP client's own timeout passed.
    /// </exception>
    public Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus, CancellationToken cancellationToken)
    {
        return HttpPostAsync<ServerCommand>("status", null, serverStatus, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="MaintenanceException">The access manager is under maintenance, or refused the connection.</exception>
    /// <exception cref="ApiException">
    /// The access manager answered with another error, or with an answer that could not be read, or that read as null
    /// where a body was expected.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or the HTTP client's own timeout passed.
    /// </exception>
    public Task<ServerConfig> Server_Configure(ServerInfo serverInfo, CancellationToken cancellationToken)
    {
        return HttpPostAsync<ServerConfig>("configure", null, serverInfo, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="KeyNotFoundException">The access manager answered 404: it knows no such token.</exception>
    /// <exception cref="MaintenanceException">The access manager is under maintenance, or refused the connection.</exception>
    /// <exception cref="ApiException">
    /// The access manager answered with another error, or with an answer that could not be read, or that read as null
    /// where a body was expected.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or the HTTP client's own timeout passed.
    /// </exception>
    public async Task<string> Acme_GetHttp01KeyAuthorization(string token, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object?> {
            { "token", token }
        };

        try {
            return await HttpGetAsync<string>("acme/http01_key_authorization", parameters, cancellationToken).Vhc();
        }
        catch (ApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound) {
            throw new KeyNotFoundException("The requested token was not found.", ex);
        }
    }

    /// <summary>Does nothing, not even for an HTTP client it made itself.</summary>
    public virtual void Dispose()
    {
    }
}