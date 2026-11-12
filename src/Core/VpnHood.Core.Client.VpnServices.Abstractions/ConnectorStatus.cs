namespace VpnHood.Core.Client.VpnServices.Abstractions;

/// <summary>
/// Represents the statistics of the client's connections to the server in a session.
/// </summary>
public class ConnectorStatus
{
    /// <summary>Gets how many idle connections the client keeps for reuse now.</summary>
    public required int FreeConnectionCount { get; init; }

    /// <summary>
    /// Gets how many times a kept connection failed when the client reused it, but by the server's error reply or a
    /// cancel.
    /// </summary>
    public required int ReusedConnectionFailedCount { get; init; }

    /// <summary>Gets how many times a kept connection carried a request to an answer without an error.</summary>
    public required int ReusedConnectionSucceededCount { get; init; }

    /// <summary>Gets how many connections the client made to the server.</summary>
    public required int CreatedConnectionCount { get; init; }

    /// <summary>Gets how many requests the server answered without an error.</summary>
    public required int RequestCount { get; init; }
}