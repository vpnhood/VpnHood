using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Requests;

/// <summary>
/// Represents a request that tells the VPN service the ad the session waits for could not be shown.
/// </summary>
public class ApiAdFailedRequest : IApiRequest
{
    /// <summary>Gets the error the ad failed with, or <see langword="null"/>.</summary>
    public required ApiError? ApiError { get; init; }
}