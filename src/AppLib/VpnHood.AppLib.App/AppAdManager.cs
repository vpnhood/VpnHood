using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.Exceptions;
using VpnHood.AppLib.Abstractions.Ads;
using VpnHood.AppLib.Abstractions.Device;
using VpnHood.AppLib.App.Services.Ads;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.VpnServices.Manager;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.AppLib.Abstractions.Ads.AdExceptions;

namespace VpnHood.AppLib.App;

public class AppAdManager(
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

        await adService.LoadInterstitialAd(uiContext, useFallback: false, cancellationToken).Vhc();
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

    /// <param name="uiContext">
    /// The window, or null where none is open: a build without ads refuses the server either way, and
    /// one with ads throws <see cref="ShowAdNoUiException" />.
    /// </param>
    public async Task ShowAd(
        IUiContext? uiContext,
        string sessionId,
        AdRequirement adRequirement,
        bool useFallback,
        CancellationToken cancellationToken)
    {
        if (adRequirement == AdRequirement.None)
            return;

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
            await vpnServiceManager.SetAdFailed(notSupportedException, cancellationToken).Vhc();
            throw notSupportedException;
        }

        // let's throw an exception if context is not set
        if (uiContext == null)
            throw new ShowAdNoUiException();

        await SetWaitForAd(cancellationToken).Vhc();

        try {
            IsShowing = true;

            // the ad network's consent form first, where one is due: the person reads it for as long as
            // they need, as the ad's timed load starts only after it
            if (consentProvider != null)
                await consentProvider.GatherConsent(uiContext, cancellationToken).Vhc();

            // flexible ad
            var adResult = adRequirement switch {
                AdRequirement.Flexible when adService.CanShowInterstitial =>
                    await adService.ShowInterstitial(uiContext, sessionId, useFallback, cancellationToken).Vhc(),
                AdRequirement.Rewarded when adService.CanShowRewarded =>
                    await adService.ShowRewarded(uiContext, sessionId, useFallback, cancellationToken).Vhc(),
                _ => throw new AdException("The requested ad is not supported in this app.")
            };

            // rewarded ad
            VhLogger.Instance.LogDebug("Successfully loaded ad. AdResult: {NetworkName}", adResult.NetworkName);

            // wait for ad post delay
            var ignoreTimeSpan = TimeSpan.FromSeconds(3);
            var delay1 = showAdPostDelay.Subtract(ignoreTimeSpan);
            if (delay1 < TimeSpan.Zero) delay1 = TimeSpan.Zero;
            await Task.Delay(delay1, cancellationToken).Vhc();

            IsWaitingForPostDelay = true;
            await Task.Delay(showAdPostDelay - delay1, cancellationToken).Vhc();

            // track tell the VpnService to disable ad mode
            await vpnServiceManager.SetAdOk(adResult,
                isRewarded: adRequirement == AdRequirement.Rewarded,
                cancellationToken).Vhc();
        }
        catch (ShowAdNoUiException) {
            // if ad is not shown because of no UI context just throw exception and let core
            // remain in WaitingForAd state
            throw;
        }
        catch (Exception ex) {
            await VerifyAdBlocker(ex, cancellationToken).Vhc();
            await vpnServiceManager.SetAdFailed(ex, cancellationToken).Vhc();
            if (adRequirement == AdRequirement.Flexible) {
                VhLogger.Instance.LogDebug(ex, "Could not show any flexible ad.");
                return;
            }

            throw;
        }
        finally {
            IsWaitingForPostDelay = false;
            IsShowing = false;
        }
    }

    public bool CanExtendByRewardedAd {
        get {
            var connectionInfo = vpnServiceManager.ConnectionInfo;

            return
                connectionInfo.SessionStatus is { CanExtendByRewardedAd: true } &&
                connectionInfo.SessionStatus.SessionExpirationTime >
                FastDateTime.UtcNow + extendByRewardedAdThreshold &&
                AdService.CanShowRewarded;
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
        await ShowAd(uiContext, connectionInfo.SessionInfo.SessionId, AdRequirement.Rewarded, useFallback: false,
            cancellationToken: cancellationToken).Vhc();
    }

    // the core takes an ad's failure only while it waits for one; sent outside that, it is lost
    private async Task SetWaitForAd(CancellationToken cancellationToken)
    {
        if (vpnServiceManager.ConnectionInfo.ClientState is not ClientState.WaitingForAd)
            await vpnServiceManager.SetWaitForAd(cancellationToken).Vhc();
    }

    private async Task VerifyAdBlocker(Exception ex, CancellationToken cancellationToken)
    {
        if (!rejectAdBlocker)
            return;

        if (await IsUnallowedPrivateDnsBySystemPrivateDns(ex, cancellationToken).Vhc())
            throw new AdBlockerException("Private DNS is not supported in free version.") {
                IsPrivateDns = true
            };
    }

    private Task<bool> IsUnallowedPrivateDnsBySystemPrivateDns(Exception ex, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _ = ex;

        // check if private DNS is active and is in allowed list.
        // A null provider means the opportunistic mode, which is the platform default and just upgrades the
        // network's own resolver to DoT; it can not be an ad blocker, so only a chosen provider is checked.
        var privateDns = uiProvider.GetPrivateDns();
        var isUnallowedPrivateDns =
            privateDns is { IsActive: true, Provider: not null } &&
            allowedPrivateDnsProviders != null &&
            !allowedPrivateDnsProviders.Contains(privateDns.Provider, StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(isUnallowedPrivateDns);
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