using System.Net;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using VpnHood.AppLogic.Api.Exceptions;
using VpnHood.AppLogic.Abstractions.Ads;
using VpnHood.AppLogic.Abstractions.Device;
using VpnHood.AppLogic.App.Services.Ads;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.VpnServices.Manager;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.AppLogic.Abstractions.Ads.AdExceptions;

namespace VpnHood.AppLogic.App;

internal class AppAdManager(
    AppAdService adService,
    VpnServiceManager vpnServiceManager,
    IDeviceUiProvider uiProvider,
    TimeSpan extendByRewardedAdThreshold,
    TimeSpan showAdPostDelay,
    bool isPreloadAdEnabled,
    bool rejectAdBlocker,
    string[]? allowedPrivateDnsProviders,
    IAdConsentProvider? consentProvider)
{
    public bool IsShowing { get; private set; }
    public AppAdService AdService => adService;
    public bool IsWaitingForPostDelay { get; private set; }
    public bool IsAdSupported => adService.CanShowInterstitial || adService.CanShowRewarded;
    public bool IsAdPrivacyOptionsRequired => consentProvider?.IsPrivacyOptionsRequired == true;

    // The next connect's interstitial, where the head preloads. A preload never brings the ad network's
    // consent form, which would open over the page the app starts on - at a first start, the first-run
    // page: where a form is due, the first ad the person waits for asks.
    public async Task PreloadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        if (!isPreloadAdEnabled)
            return;

        if (consentProvider != null && !await consentProvider.CanRequestAds(uiContext, cancellationToken).Vhc()) {
            VhLogger.Instance.LogInformation("No ad is preloaded before the person answers the ad consent form.");
            return;
        }

        await adService.LoadInterstitialAd(uiContext, cancellationToken).Vhc();
    }

    // the ad network's consent form again, for the person to change their answer
    public Task ShowAdPrivacyOptions(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        if (consentProvider == null)
            throw new InvalidOperationException("This app has no ad consent to change.");

        if (uiContext == null)
            throw new UiContextNotAvailableException();

        return consentProvider.ShowPrivacyOptions(uiContext, cancellationToken);
    }

    // Shows the ad the session waits for, on its route, and again on each route the session moves to after a failed
    // try (AdRoute): the session owns their order and decides what a failure means, a stop ending a connect at once.
    // Returns the failure, which the session has been told, or null where an ad showed. Throws what the session was
    // not told: no window (uiContext null, where none is open), so the session keeps waiting for one, or a result that
    // never reached it, which the app's caller ends the connection for
    public Task<Exception?> ShowAd(
        IUiContext? uiContext,
        string sessionId,
        AdRequirement adRequirement,
        CancellationToken cancellationToken)
    {
        return ShowAd(uiContext, sessionId, adRequirement, isExtension: false, cancellationToken);
    }

    // isExtension: an ad that extends the session, whose wait nothing else ends
    private async Task<Exception?> ShowAd(IUiContext? uiContext, string sessionId, AdRequirement adRequirement,
        bool isExtension, CancellationToken cancellationToken)
    {
        if (adRequirement == AdRequirement.None)
            return null;

        // check if ad is already showing
        if (IsShowing)
            throw new InvalidOperationException("An ad is already being shown.");

        VhLogger.Instance.LogDebug("Showing ad. Session: {SessionId}, AdRequirement: {AdRequirement}", sessionId,
            adRequirement);

        // A build that shows no ads says so before it asks for a window: the desktop service never has
        // one, and "the window was not open" would send the person after the wrong cause
        if (!IsAdSupported) {
            var notSupportedException = new AdException("This app does not support servers that require ads.");
            await SetWaitForAd(cancellationToken).Vhc();
            await vpnServiceManager.SetAdFailed(notSupportedException, tryNextRoute: false, cancellationToken).Vhc();
            return notSupportedException;
        }

        // let's throw an exception if context is not set
        if (uiContext == null)
            throw new ShowAdNoUiException();

        await SetWaitForAd(cancellationToken).Vhc();

        try {
            IsShowing = true;
            return await ShowOnRoutes(uiContext, sessionId, adRequirement, cancellationToken).Vhc();
        }
        catch (Exception ex) when (isExtension) {
            // no window comes back for it, and its wait would keep the session's new traffic around the tunnel
            await TryStopWait(ex).Vhc();
            throw;
        }
        finally {
            IsWaitingForPostDelay = false;
            IsShowing = false;
        }
    }

    // The session moves to its next route as it takes a failure, so the answer to the failure shows the route to try
    // next; one it has not reached, as the routes after the VPN starts, comes with a state change of its own. A session
    // still on the route the try failed on never got the result, as one lost between the two processes
    private async Task<Exception?> ShowOnRoutes(IUiContext uiContext, string sessionId, AdRequirement adRequirement,
        CancellationToken cancellationToken)
    {
        if (vpnServiceManager.ConnectionInfo.SessionStatus is not { AdRoute: { } route } sessionStatus)
            throw new InvalidOperationException("The session waits for no ad.");

        var isLastRoute = sessionStatus.IsLastAdRoute;
        while (true) {
            var failure = await TryShowOnRoute(uiContext, sessionId, adRequirement, route, isLastRoute,
                cancellationToken).Vhc();

            if (failure == null)
                return null;

            if (vpnServiceManager.ConnectionInfo is not {
                    ClientState: ClientState.WaitingForAd, SessionStatus: { AdRoute: { } nextRoute } nextStatus
                })
                return failure;

            if (nextRoute == route)
                ExceptionDispatchInfo.Throw(failure);

            route = nextRoute;
            isLastRoute = nextStatus.IsLastAdRoute;
        }
    }

    // One try: null where the ad showed and the session took it; else the ad's failure, which the session has been
    // told, or the session's refusal of an ad that showed, as a rewarded ad's server refused, which it knows already
    private async Task<Exception?> TryShowOnRoute(IUiContext uiContext, string sessionId,
        AdRequirement adRequirement, AdRoute route, bool isLastRoute, CancellationToken cancellationToken)
    {
        try {
            var adResult = await ShowOnRoute(uiContext, sessionId, adRequirement, route, isLastRoute, cancellationToken)
                .Vhc();

            return await TryReportShown(adResult, adRequirement, cancellationToken).Vhc();
        }
        catch (ShowAdNoUiException) {
            // the session keeps waiting, for a window to show it in
            throw;
        }
        catch (Exception ex) {
            // an ad blocker the app refuses fails every route: a stop, which ends a connect too
            var adBlockerException = GetAdBlockerException();
            if (adBlockerException != null) {
                await vpnServiceManager.SetAdFailed(adBlockerException, tryNextRoute: false, cancellationToken).Vhc();
                return adBlockerException;
            }

            VhLogger.Instance.LogDebug(ex, "Could not show an ad on its route. Route: {Route}", route);
            await vpnServiceManager.SetAdFailed(ex, tryNextRoute: true, cancellationToken).Vhc();
            return ex;
        }
    }

    // The ad from the networks the route takes, and its post delay, during which the app shows the connection as made
    // while the session still routes for the ad (IsWaitingForPostDelay)
    private async Task<AdResult> ShowOnRoute(IUiContext uiContext, string sessionId, AdRequirement adRequirement,
        AdRoute route, bool isLastRoute, CancellationToken cancellationToken)
    {
        // the ad network's consent form first, where one is due: the person reads it for as long as
        // they need, as the ad's timed load starts only after it
        if (consentProvider != null)
            await consentProvider.GatherConsent(uiContext, cancellationToken).Vhc();

        var adResult = adRequirement switch {
            AdRequirement.Flexible when adService.CanShowInterstitial =>
                await adService.ShowInterstitial(uiContext, sessionId, route, isLastRoute, cancellationToken).Vhc(),
            AdRequirement.Rewarded when adService.CanShowRewarded =>
                await adService.ShowRewarded(uiContext, sessionId, route, isLastRoute, cancellationToken).Vhc(),
            _ => throw new AdException("The requested ad is not supported in this app.")
        };

        VhLogger.Instance.LogDebug("Successfully loaded ad. AdResult: {NetworkName}, Route: {Route}",
            adResult.NetworkName, route);

        // wait for ad post delay
        var ignoreTimeSpan = TimeSpan.FromSeconds(3);
        var delay1 = showAdPostDelay.Subtract(ignoreTimeSpan);
        if (delay1 < TimeSpan.Zero) delay1 = TimeSpan.Zero;
        await Task.Delay(delay1, cancellationToken).Vhc();

        IsWaitingForPostDelay = true;
        await Task.Delay(showAdPostDelay - delay1, cancellationToken).Vhc();
        return adResult;
    }

    // The session's answer to an ad that showed. A refusal ended its wait already, so it is returned and never told
    // again: a failure told later could end the session's next wait
    private async Task<Exception?> TryReportShown(AdResult adResult, AdRequirement adRequirement,
        CancellationToken cancellationToken)
    {
        try {
            await vpnServiceManager.SetAdOk(adResult,
                isRewarded: adRequirement == AdRequirement.Rewarded,
                cancellationToken).Vhc();

            return null;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "The session refused an ad that showed.");
            return ex;
        }
    }

    public bool CanExtendByRewardedAd {
        get {
            var connectionInfo = vpnServiceManager.ConnectionInfo;

            return
                connectionInfo.SessionStatus is { CanExtendByRewardedAd: true } &&
                connectionInfo.SessionStatus.SessionExpirationTime >
                FastDateTime.UtcNow + extendByRewardedAdThreshold &&
                AdService.CanShowRewardedInRegion;
        }
    }

    public async Task ExtendByRewardedAd(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        // save variable to prevent null reference exception
        var connectionInfo = vpnServiceManager.ConnectionInfo;

        if (!AdService.CanShowRewarded)
            throw new InvalidOperationException("Rewarded ad is not supported in this app.");

        if (connectionInfo.SessionInfo == null || connectionInfo is not { ClientState: ClientState.Connected })
            throw new InvalidOperationException("Could not show ad. The VPN is not connected.");

        if (!CanExtendByRewardedAd)
            throw new InvalidOperationException("Could not extend this session by a rewarded ad at this time.");

        // set wait for ad state
        var failure = await ShowAd(uiContext, connectionInfo.SessionInfo.SessionId, AdRequirement.Rewarded,
            isExtension: true, cancellationToken).Vhc();

        if (failure != null)
            ExceptionDispatchInfo.Throw(failure);
    }

    // the core takes an ad's failure only while it waits for one; sent outside that, it is lost
    private async Task SetWaitForAd(CancellationToken cancellationToken)
    {
        if (vpnServiceManager.ConnectionInfo.ClientState is not ClientState.WaitingForAd)
            await vpnServiceManager.SetWaitForAd(cancellationToken).Vhc();
    }

    // A stop for the session's wait, its own failure told; a failed request, as the one before it may have, leaves only
    // a log. A deadline of its own, as the caller's token may be canceled already: 30 seconds ends only a request the
    // service never answers, which has no limit of its own
    private async Task TryStopWait(Exception ex)
    {
        try {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await vpnServiceManager.SetAdFailed(ex, tryNextRoute: false, timeoutCts.Token).Vhc();
        }
        catch (Exception stopException) {
            VhLogger.Instance.LogWarning(stopException, "Could not end the session's wait for the ad.");
        }
    }

    // the failed ad's cause where the app refuses it: a private DNS that may block ads, or null
    private AdBlockerException? GetAdBlockerException()
    {
        if (!rejectAdBlocker || !IsUnallowedPrivateDns())
            return null;

        return new AdBlockerException("Private DNS is not supported in free version.") {
            IsPrivateDns = true
        };
    }

    private bool IsUnallowedPrivateDns()
    {
        // check if private DNS is active and is in allowed list.
        // A null provider means the opportunistic mode, which is the platform default and just upgrades the
        // network's own resolver to DoT; it can not be an ad blocker, so only a chosen provider is checked.
        var privateDns = uiProvider.GetPrivateDns();
        return privateDns is { IsActive: true, Provider: not null } &&
               allowedPrivateDnsProviders != null &&
               !allowedPrivateDnsProviders.Contains(privateDns.Provider, StringComparer.OrdinalIgnoreCase);
    }

    // ReSharper disable once UnusedMember.Local
    private async Task<bool> IsAdBlockerByCheckDnsRequest(Exception ex, CancellationToken cancellationToken)
    {
        // make sure we have the latest state
        await vpnServiceManager.RefreshState(cancellationToken).Vhc();
        var connectionInfo = vpnServiceManager.ConnectionInfo;

        // ignore ad blocker if secure DNS (DoT/DoQ) is not detected or if the adapter is not started yet.
        // We don't count network and default dns as ad blocker because they can be set by network admin
        // WaitingForAdEx means adapter is started and network DNS is set to VPN DNS servers.
        if (connectionInfo.SessionStatus is null ||
            !connectionInfo.SessionStatus.IsAdapterStarted ||
            !connectionInfo.SessionStatus.IsSecureDnsDetected)
            return false;

        // we just check if the exception is LoadAdException
        if (ex is not LoadAdException)
            return false;

        // NoFill usually means the network simply had no inventory.
        // Because secure DNS is detected, we double-check via DNS to rule out blocking.
        if (ex is NoFillAdException)
            return await CheckAdBlockerByDnsQuery(cancellationToken).Vhc();

        // secure DNS is detected and exception is LoadAdException so we can assume that ad blocker exists
        return true;
    }

    private static async Task<bool> CheckAdBlockerByDnsQuery(CancellationToken cancellationToken)
    {
        const string adDomain = "googleads.g.doubleclick.net";

        // check google dns
        try {
            var hostEntry =
                await DnsUtils.GetHostEntry(
                    adDomain,
                    new IPEndPoint(IPAddressUtil.GoogleDnsServers.First(), 53),
                    TimeSpan.FromSeconds(4), cancellationToken).Vhc();

            // it shouldn't occured usually, as we expect it pass through VPN
            if (hostEntry.AddressList.Any(x => x.Equals(IPAddress.Any) || x.Equals(IPAddress.IPv6Any)))
                return true; // definitely ad blocker
        }
        catch (Exception) {
            return false; // unknown state
        }

        // check default dns
        try {
            var hostEntry = await Dns.GetHostEntryAsync(adDomain, cancellationToken).Vhc();
            return hostEntry.AddressList
                .Any(x => x.Equals(IPAddress.Any) || x.Equals(IPAddress.IPv6Any)); // definitely ad blocker
        }
        catch {
            return true; // perhaps ad blocker, because we couldn't resolve the domain with UDP
        }
    }
}