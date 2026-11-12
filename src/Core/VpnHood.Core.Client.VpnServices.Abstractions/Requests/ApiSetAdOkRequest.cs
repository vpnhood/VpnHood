using VpnHood.Core.Client.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Abstractions.Requests;

/// <summary>
/// Represents a request that tells the VPN service the ad the session waits for was shown.
/// </summary>
public class ApiSetAdOkRequest : IApiRequest
{
    /// <summary>
    /// Gets a value indicating whether the ad was a rewarded one, whose data the service sends the server where it has
    /// any.
    /// </summary>
    public required bool IsRewarded { get; init; }

    /// <summary>Gets the ad's result.</summary>
    public required AdResult AdResult { get; init; }
}