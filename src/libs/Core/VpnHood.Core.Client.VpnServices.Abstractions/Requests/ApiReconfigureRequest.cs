namespace VpnHood.Core.Client.VpnServices.Abstractions.Requests;

/// <summary>
/// Represents a request that changes the settings of the running connection.
/// </summary>
/// <remarks>
/// One that comes before the session is up is kept, and the connect applies it once it is. The session also takes the
/// split filters the app rewrote in the config folder before it sent this.
/// </remarks>
public class ApiReconfigureRequest : IApiRequest
{
    /// <summary>Gets the settings.</summary>
    public required ClientReconfigureParams Params { get; init; }
}