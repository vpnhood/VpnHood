using VpnHood.Net.Toolkit.ApiClients;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Requests;

/// <summary>
/// Represents a request that tells the VPN service the ad the session waits for could not be shown.
/// </summary>
public class ApiAdFailedRequest : IApiRequest
{
    /// <summary>Gets the error the ad failed with, or <see langword="null"/>.</summary>
    public required ApiError? ApiError { get; init; }

    /// <summary>
    /// Gets a value indicating whether the session tries the ad on its next route, where one is left
    /// (<see cref="VpnHood.Core.Client.Abstractions.AdRoute"/>), or ends its wait with the failure;
    /// <see langword="false"/> ends a connect too, its routes after the VPN untried.
    /// </summary>
    public required bool TryNextRoute { get; init; }
}