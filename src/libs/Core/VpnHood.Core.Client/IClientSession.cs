using VpnHood.Core.Client.Abstractions;

namespace VpnHood.Core.Client;

/// <summary>Represents a client's session with a server.</summary>
public interface IClientSession
{
    /// <summary>
    /// Gets the session's information: what the server gave at the session's start, with what the client worked out of
    /// it, as its DNS plan.
    /// </summary>
    SessionInfo Info { get; }

    /// <summary>Gets the session's status now.</summary>
    ISessionStatus Status { get; }

    /// <summary>Gets the session's handler of ads.</summary>
    ISessionAdHandler AdHandler { get; }

    internal ClientSessionConfig Config { get; }
}