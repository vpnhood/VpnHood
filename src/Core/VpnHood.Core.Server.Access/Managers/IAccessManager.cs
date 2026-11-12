using System.Net;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Server.Access.Messaging;

namespace VpnHood.Core.Server.Access.Managers;

/// <summary>
/// Defines the access manager a VPN server answers to: it admits sessions, counts their traffic, and configures the
/// server.
/// </summary>
public interface IAccessManager : IDisposable
{
    /// <summary>Creates a session for a client's hello.</summary>
    /// <param name="sessionRequestEx">The client's request, with what the server knows of the client.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the session, or its refusal in its error code.</returns>
    Task<SessionResponseEx> Session_Create(SessionRequestEx sessionRequestEx, CancellationToken cancellationToken);

    /// <summary>Gets a session the server does not hold in memory, as after a restart, to take it up again.</summary>
    /// <param name="sessionId">The session's ID.</param>
    /// <param name="hostEndPoint">The server's endpoint the client came to.</param>
    /// <param name="clientIp">The client's IP, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the session, or its error in its error code.</returns>
    Task<SessionResponseEx> Session_Get(ulong sessionId, IPEndPoint hostEndPoint, IPAddress? clientIp, CancellationToken cancellationToken);

    /// <summary>Gets the server's sessions, to take them up again at its start.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the sessions.</returns>
    Task<SessionResponseEx[]> Session_GetAll(CancellationToken cancellationToken);

    /// <summary>Adds a session's traffic, and a rewarded ad's data, without closing it.</summary>
    /// <param name="sessionId">The session's ID.</param>
    /// <param name="traffic">The session's traffic since its last report.</param>
    /// <param name="adData">A rewarded ad's data, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the session's state.</returns>
    Task<SessionResponse> Session_AddUsage(ulong sessionId, Traffic traffic, string? adData, CancellationToken cancellationToken);

    /// <summary>Adds the traffic of many sessions, and their closes, in one call.</summary>
    /// <param name="sessionUsages">The sessions' usage.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task whose result is each session's state, by its ID; the server sends again no entry the answer leaves out or
    /// answers with an error.
    /// </returns>
    Task<Dictionary<ulong, SessionResponse>> Session_AddUsages(SessionUsage[] sessionUsages, CancellationToken cancellationToken);

    /// <summary>Closes a session with its last traffic.</summary>
    /// <param name="sessionId">The session's ID.</param>
    /// <param name="traffic">The session's traffic since its last report.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the session's last state.</returns>
    Task<SessionResponse> Session_Close(ulong sessionId, Traffic traffic, CancellationToken cancellationToken);

    /// <summary>Sends the server's status, with its sessions' usage, and gets what the access manager wants of it.</summary>
    /// <param name="serverStatus">The status.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the access manager's answer: the configuration's code and the sessions' states.</returns>
    Task<ServerCommand> Server_UpdateStatus(ServerStatus serverStatus, CancellationToken cancellationToken);

    /// <summary>Sends what the server is and has, and gets its configuration.</summary>
    /// <param name="serverInfo">The server's info.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the configuration.</returns>
    Task<ServerConfig> Server_Configure(ServerInfo serverInfo, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the key authorization of an ACME HTTP-01 challenge's token, which the server serves on port 80.
    /// </summary>
    /// <param name="token">The challenge's token.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the key authorization.</returns>
    /// <exception cref="KeyNotFoundException">
    /// The access manager knows no such token, or serves no HTTP-01 challenge.
    /// </exception>
    Task<string> Acme_GetHttp01KeyAuthorization(string token, CancellationToken cancellationToken);
}