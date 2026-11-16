namespace VpnHood.Core.Common.Messaging;

/// <summary>
/// Provides the base of a client's request to a server.
/// </summary>
/// <param name="requestCode">The code of the request's kind.</param>
public abstract class ClientRequest(byte requestCode)
{
    /// <summary>Gets the code of the request's kind.</summary>
    public byte RequestCode { get; } = requestCode;

    /// <summary>Gets or sets the request's ID, which its logs carry.</summary>
    public required string RequestId { get; set; }

    /// <summary>Gets when the client made the request, by its own clock, in UTC. The default is the time it was made.</summary>
    // init, so the server reads the client's own time rather than stamping the moment it reads the request
    public DateTime RequestTime { get; init; } = DateTime.UtcNow;
}