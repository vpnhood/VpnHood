namespace VpnHood.AppLib.Api;

/// <summary>Represents the app's API as one object: its six parts, whichever way a UI reaches them.</summary>
/// <remarks>
/// A UI reaches it in the app's own process, where no listener is involved, or over HTTP from a paired browser
/// (<c>VpnHoodApiHttpFactory</c>); a UI written against it runs on the device and in a browser alike. A failure comes
/// as <see cref="IAppApi"/> describes.
/// </remarks>
/// <param name="app">The app's information, state and settings.</param>
/// <param name="vpnProfiles">The VPN profiles.</param>
/// <param name="account">The person's account.</param>
/// <param name="billing">The in-app purchase.</param>
/// <param name="intents">What the UI asks of the device.</param>
/// <param name="proxyEndPoints">The proxy servers.</param>
public sealed class VpnHoodApi(
    IAppApi app,
    IVpnProfilesApi vpnProfiles,
    IAccountApi account,
    IBillingApi billing,
    IIntentsApi intents,
    IProxyEndPointsApi proxyEndPoints)
{
    /// <summary>Gets the app's information, state and settings.</summary>
    public IAppApi App { get; } = app;

    /// <summary>Gets the VPN profiles.</summary>
    public IVpnProfilesApi VpnProfiles { get; } = vpnProfiles;

    /// <summary>Gets the person's account.</summary>
    public IAccountApi Account { get; } = account;

    /// <summary>Gets the in-app purchase.</summary>
    public IBillingApi Billing { get; } = billing;

    /// <summary>Gets what the UI asks of the device.</summary>
    public IIntentsApi Intents { get; } = intents;

    /// <summary>Gets the proxy servers.</summary>
    public IProxyEndPointsApi ProxyEndPoints { get; } = proxyEndPoints;
}
