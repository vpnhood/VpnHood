namespace VpnHood.Core.Client.VpnServices.Abstractions.Requests;

/// <summary>
/// Represents a request that tells the VPN service the app is about to show the ad the session requires, so the
/// session waits for it; the service answers at once, and the app sends <see cref="ApiSetAdOkRequest"/> or
/// <see cref="ApiAdFailedRequest"/> once the ad is done.
/// </summary>
public class ApiSetWaitForAdRequest : IApiRequest;