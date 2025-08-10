using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Abstractions.AdExceptions;
using VpnHood.AppLib.Exceptions;
using VpnHood.AppLib.Services.Ads;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Client.VpnServices.Manager;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib;

public class AppAdManager(
    AppAdService adService,
    VpnServiceManager vpnServiceManager,
    TimeSpan extendByRewardedAdThreshold,
    TimeSpan showAdPostDelay,
    bool isPreloadAdEnabled,
    bool detectAdBlocker)
{
    public bool IsShowing { get; private set; }
    public AppAdService AdService => adService;
    public bool IsWaitingForPostDelay { get; private set; }
    public bool IsPreloadAdEnabled => isPreloadAdEnabled;

    public async Task ShowAd(
        string sessionId,
        AdRequirement adRequirement,
        CancellationToken cancellationToken)
    {
        if (adRequirement == AdRequirement.None)
            return;

        // check if ad is already showing
        if (IsShowing)
            throw new InvalidOperationException("An ad is already being shown.");

        VhLogger.Instance.LogDebug("Showing ad. Session: {SessionId}, AdRequirement: {AdRequirement}", sessionId, adRequirement);

        // let's throw an exception if context is not set
        var uiContext = AppUiContext.Context;
        if (uiContext == null)
            throw new ShowAdNoUiException();

        // set wait for ad state
        await vpnServiceManager.SetWaitForAd(cancellationToken).Vhc();

        try {
            IsShowing = true;

            // flexible ad
            var adResult = adRequirement switch {
                AdRequirement.Flexible =>
                    await adService.ShowInterstitial(uiContext, sessionId, cancellationToken).Vhc(),
                AdRequirement.Rewarded =>
                    await adService.ShowRewarded(uiContext, sessionId, cancellationToken).Vhc(),
                _ => throw new AdException("Unsupported ad requirement.")
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
        catch (LoadAdException ex) 
            when (adRequirement == AdRequirement.Flexible || 
                  vpnServiceManager.ConnectionInfo.ClientState is not ClientState.WaitingForAdEx) {

            await vpnServiceManager.RefreshState(cancellationToken).Vhc();
            await VerifyAdBlocker(ex, cancellationToken).Vhc();
            await vpnServiceManager.SetAdFailed(ex, cancellationToken).Vhc();
            VhLogger.Instance.LogDebug(ex, "Could not load any flexible ad.");
        }
        catch (Exception ex) {
            await vpnServiceManager.RefreshState(cancellationToken).Vhc();
            await VerifyAdBlocker(ex, cancellationToken).Vhc();
            await vpnServiceManager.SetAdFailed(ex, cancellationToken).Vhc();
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
                connectionInfo.SessionStatus.SessionExpirationTime > FastDateTime.UtcNow + extendByRewardedAdThreshold &&
                AdService.CanShowRewarded;
        }
    }

    public async Task ExtendByRewardedAd(CancellationToken cancellationToken)
    {
        // save variable to prevent null reference exception
        var connectionInfo = vpnServiceManager.ConnectionInfo;

        if (AdService.CanShowRewarded != true)
            throw new InvalidOperationException("Rewarded ad is not supported in this app.");

        if (connectionInfo.SessionInfo == null || connectionInfo is not { ClientState: ClientState.Connected })
            throw new InvalidOperationException("Could not show ad. The VPN is not connected.");

        if (!CanExtendByRewardedAd)
            throw new InvalidOperationException("Could not extend this session by a rewarded ad at this time.");

        // set wait for ad state
        await ShowAd(connectionInfo.SessionInfo.SessionId, AdRequirement.Rewarded, 
            cancellationToken: cancellationToken).Vhc();
    }

    private async Task VerifyAdBlocker(Exception ex, CancellationToken cancellationToken)
    {
        if (await IsAdBlocker(ex, cancellationToken).Vhc())
            throw new AdBlockerException("Private DNS is not supported in free version.") {
                IsPrivateDns = true
            };
    }

    private async Task<bool> IsAdBlocker(Exception ex, CancellationToken cancellationToken)
    {
        if (!detectAdBlocker)
            return false; // ad blocker detection is disabled

        var connectionInfo = vpnServiceManager.ConnectionInfo;

        // ignore ad blocker if DNS over TLS is not detected or if the client state is not WaitingForAdEx.
        // We don't count network and default dns as ad blocker because they can be set by network admin
        // WaitingForAdEx means adapter is started and network DNS is set to VPN DNS servers.
        if (connectionInfo.ClientState is not ClientState.WaitingForAdEx ||
            connectionInfo.SessionStatus?.IsDnsOverTlsDetected is null or false)
            return false;

        // we just check if the exception is LoadAdException
        if (ex is not LoadAdException)
            return false;

        // NoFill usually means the network simply had no inventory.
        // Because DoT is detected, we double-check via DNS to rule out blocking.
        if (ex is NoFillAdException)
            return await CheckAdBlockerByDnsQuery(cancellationToken).Vhc();

        // IsDnsOverTlsDetected exists and exception is LoadAdException so we can assume that ad blocker exists
        return true;
    }

    private static async Task<bool> CheckAdBlockerByDnsQuery(CancellationToken cancellationToken)
    {
        var nullAddress = IPAddress.Parse("0.0.0.0");
        const string adDomain = "googleads.g.doubleclick.net";

        // check google dns
        try {
            var hostEntry =
                await DnsResolver.GetHostEntry(
                    adDomain,
                    new IPEndPoint(IPAddressUtil.GoogleDnsServers.First(), 53),
                    TimeSpan.FromSeconds(4), cancellationToken).Vhc();

            // it shouldn't occured usually, as we expect it pass through VPN
            if (hostEntry.AddressList.Any(x => x.Equals(nullAddress)))
                return true; // definitely ad blocker
        }
        catch (Exception) {
            return false; // unknown state
        }

        // check default dns
        try {
            var hostEntry = await Dns.GetHostEntryAsync(adDomain, cancellationToken).Vhc();
            return hostEntry.AddressList.Any(x => x.Equals(nullAddress)); // definitely ad blocker
        }
        catch {
            return true; // perhaps ad blocker, because we couldn't resolve the domain with UDP
        }
    }
}

