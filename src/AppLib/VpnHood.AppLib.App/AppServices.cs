using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Abstractions.Device;
using VpnHood.AppLib.App.Services;
using VpnHood.AppLib.App.Services.Accounts;
using VpnHood.AppLib.App.Services.Proxies;
using VpnHood.AppLib.App.Services.Trackers;
using VpnHood.AppLib.App.Services.Updaters;

namespace VpnHood.AppLib.App;

/// <summary>
/// Provides the app's services and the providers it works through (<see cref="VpnHoodApp.Services"/>), which the app
/// makes and sets once, at its start.
/// </summary>
public class AppServices : IDisposable
{
    /// <summary>
    /// Gets the account's service, or <see langword="null"/> for an app with no accounts
    /// (<see cref="AppOptions.AccountProvider"/>).
    /// </summary>
    public required AccountService? AccountService { get; init; }

    /// <summary>
    /// Gets the updates' service, or <see langword="null"/> for an app that looks for none
    /// (<see cref="AppOptions.UpdaterOptions"/>).
    /// </summary>
    public required AppUpdaterService? UpdaterService { get; init; }

    /// <summary>Gets the proxies' service.</summary>
    public required AppProxyEndPointService ProxyEndPointService { get; init; }

    /// <summary>Gets the service of the split by country.</summary>
    public required SplitCountryService SplitCountryService { get; init; }

    /// <summary>Gets the service of the split by IP address that the app applies.</summary>
    public required SplitIpViaAppService SplitIpViaAppService { get; init; }

    /// <summary>Gets the service of the split by domain.</summary>
    public required SplitDomainService SplitDomainService { get; init; }

    /// <summary>Gets the service that hands the splits' databases to the VPN service.</summary>
    public required SplitDbPublisherService SplitDbPublisherService { get; init; }

    /// <summary>
    /// Gets what the app asks of the device's own UI: the head's (<see cref="AppOptions.DeviceUiProvider"/>), or a
    /// <see cref="NullDeviceUiProvider"/>, which supports none of it but takes a web browser as present.
    /// </summary>
    public required IDeviceUiProvider DeviceUiProvider { get; init; }

    /// <summary>
    /// Gets how the app reads and sets its language: the head's (<see cref="AppOptions.CultureProvider"/>), or the
    /// app's own, which keeps it in the settings.
    /// </summary>
    public required IAppCultureProvider CultureProvider { get; init; }

    /// <summary>
    /// Gets the store's in-app review (<see cref="AppOptions.UserReviewProvider"/>), or <see langword="null"/>.
    /// </summary>
    public required IAppUserReviewProvider? UserReviewProvider { get; init; }

    /// <summary>Gets the trackers' service.</summary>
    public required AppTrackerService TrackerService { get; init; }

    /// <inheritdoc/>
    /// <remarks>It disposes the updates' service, the one service it owns.</remarks>
    public void Dispose()
    {
        UpdaterService?.Dispose();
    }
}