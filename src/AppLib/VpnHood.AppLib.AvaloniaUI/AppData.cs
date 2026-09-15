using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.Settings;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Common.Messaging;

namespace VpnHood.AppLib.AvaloniaUI;

// What the pages read off the app, the way the web UI's VpnHoodAppData reads it off the API: the
// same questions with the same answers, so a page ported from there asks nothing new. Every
// reading is live - there is no copy to go stale - and State is built by the app on each read, so
// a page that reads several of its fields takes it once.
public static class AppData
{
    private static VpnHoodApp App => VpnHoodApp.Instance;

    public static AppFeatures Features => App.Features;
    public static UserSettings UserSettings => App.UserSettings;
    public static AppState State => App.State;

    // what the device can open or ask for (the web UI's intentFeatures), read off the providers
    public static DeviceIntentFeatures Intents => new(App.Services.DeviceUiProvider, App.Services.UserReviewProvider);

    // The account, as the web UI keeps it in UserState: read once at start and after a sign-in,
    // sign-out or purchase, never polled (account.vue says why).
    public static Account? Account { get; private set; }

    public static async Task LoadAccount(bool withRefresh, CancellationToken cancellationToken)
    {
        var service = App.Services.AccountService;
        if (service == null) {
            Account = null;
            return;
        }

        if (withRefresh) {
            try {
                await service.Refresh(cancellationToken);
            }
            catch (Exception) {
                // best effort: the portal is often what is blocked, and the saved account still shows
            }
        }

        Account = await service.GetAccount(cancellationToken);
    }

    // The TV layout: this UI runs on the device itself, never over the LAN, so the device's word
    // is the whole answer (the web UI's isTvUi also asks whether the request came from another
    // device; nothing here does).
    public static bool IsTvUi => Features.IsTv;
    public static bool IsConnectApp => AppProduct.IsConnect(Features.UiName);
    public static bool IsSingleProfileMode => AppProduct.IsSingleProfileMode(Features.UiName);

    public static bool IsConnected(AppState state)
    {
        return state.ConnectionState is AppConnectionState.Connected or AppConnectionState.Unstable;
    }

    public static bool IsConnected() => IsConnected(State);

    // A build with no premium tier is the full app: nothing sold, no crown, everything allowed.
    public static bool IsPremiumSupported => Features.Premium != null;
    public static bool IsPremiumUser => App.CurrentClientProfileInfo?.IsPremium == true;
    public static bool IsPremiumByAccount => IsPremiumUser && Account?.Subscription != null;
    public static bool IsPremiumByCode => IsPremiumUser && !IsPremiumByAccount;
    public static bool CanImportAccessCode => App.CurrentClientProfileInfo?.CanImportAccessCode == true;
    public static bool CanViewAccessCode => App.CurrentClientProfileInfo?.CanViewAccessCode == true;
    public static bool CanTryPremium => App.CurrentClientProfileInfo?.CanTryPremium == true;
    public static bool CanGoPremium => App.CurrentClientProfileInfo?.CanGoPremium == true;
    public static Guid? ClientProfileId => App.CurrentClientProfileInfo?.ClientProfileId ?? UserSettings.ClientProfileId;

    public static bool IsPremiumFeature(AppFeature feature)
    {
        return Features.Premium?.Features.Contains(feature) ?? false;
    }

    public static bool IsPremiumFeatureAllowed(AppFeature feature)
    {
        return App.IsPremiumFeatureAllowed(feature);
    }

    // the crown marks what this session does not have yet: a feature a tier sells, a location's
    // premium plan (PremiumIcon.vue)
    public static bool ShowCrown(bool isPremium)
    {
        return isPremium && !IsPremiumUser;
    }

    public static bool ShowCrown(AppFeature feature)
    {
        return ShowCrown(IsPremiumFeature(feature));
    }

    public static bool IsPrivateDnsActive(AppState state) => state.SystemPrivateDns?.IsActive == true;
    public static bool IsPrivateDnsCustomized(AppState state) => !string.IsNullOrEmpty(state.SystemPrivateDns?.Provider);

    public static bool IsDnsCustomized(AppState state)
    {
        if (IsPrivateDnsCustomized(state))
            return true;
        return UserSettings.DnsMode == DnsMode.AdapterDns && IsPremiumFeatureAllowed(AppFeature.CustomDns);
    }

    public static bool IsCustomEndpointActive(AppState state)
    {
        var profile = state.ClientProfile;
        return profile is { IsCustomServerEndpointsEnabled: true, CustomServerEndpoints.Length: > 0 };
    }

    public static bool IsNotificationEnabled(AppState state) => state.IsNotificationEnabled == true;

    public static ChannelProtocol ActiveProtocol(AppState state)
    {
        return IsConnected(state) ? state.ChannelProtocol : UserSettings.ChannelProtocol;
    }

    public static bool IsProtocolEnabled(AppState state, ChannelProtocol protocol)
    {
        return state.SessionInfo != null
            ? state.SessionInfo.ChannelProtocols.Contains(protocol)
            : IsShowProtocol(protocol);
    }

    public static bool IsShowProtocol(ChannelProtocol protocol)
    {
        return Features.ChannelProtocols.Contains(protocol);
    }

    public static string ProtocolTitle(ChannelProtocol protocol)
    {
        return protocol switch {
            ChannelProtocol.Udp => Strings.Current.ProtocolUdp,
            ChannelProtocol.Quic => Strings.Current.ProtocolQuic,
            _ => Strings.Current.ProtocolTcp
        };
    }

    // this head has no web analytics of its own, so the tracker the app reports is the whole answer
    public static bool IsAnonymousTrackerSupported => Features.IsAnonymousTrackerSupported;

    public static bool IsLocalNetworkAvailable(AppState state)
    {
        return !IsConnected(state) || state.SessionInfo?.IsLocalNetworkAllowed == true;
    }

    public static bool IsLocationAutoSelected(string? serverLocation)
    {
        return serverLocation is "*" or "*/*";
    }

    // The home row's word for the countries split, from the EFFECTIVE mode in the state: a split
    // the toggle or the plan silenced reads Off (VpnHoodAppData.splitCountryStatusText).
    public const int AllCountriesCount = 238;
    private const int MaxFlags = 3;

    public static string SplitCountryStatusText(AppState state)
    {
        var split = state.SplitTunnelingState;
        switch (split.CountryMode) {
            case SplitCountryMode.ExcludeMyCountry:
                return Strings.Current.ExcludeMyCountry;
            case SplitCountryMode.ExcludeList: {
                var count = split.Countries.Count;
                if (count == 0) return Strings.Current.Off;
                if (count < MaxFlags) return Strings.Current.Exclude;
                if (count < AllCountriesCount / 2) return Strings.Current.AllExceptX(count);
                return Strings.Current.OnlyX(AllCountriesCount - count);
            }
            default:
                return Strings.Current.Off;
        }
    }

    public static string SplitAppsStatusText()
    {
        var split = UserSettings.SplitTunneling;
        return split.AppMode switch {
            SplitAppMode.Exclude => split.Apps.Length > 0 ? Strings.Current.AllExceptX(split.Apps.Length) : Strings.Current.Off,
            SplitAppMode.Include => Strings.Current.OnlyX(split.Apps.Length),
            _ => Strings.Current.Off
        };
    }

    public static bool HasDebugCommand(string command)
    {
        return App.HasDebugCommand(command);
    }

    // Android only: the tools drive the Starlink app, which exists on no other platform; a debug
    // build keeps the page reachable while it is developed
    public static bool IsStarlinkToolsEnabled =>
        HasDebugCommand("/starlink") && (Features.IsDebugMode || Features.OsType == AppOsType.Android);

    // the store's method, never the portal's password (VpnHoodApp.primaryProviderId)
    public static string? PrimaryProviderId => Features.AuthProviderIds.FirstOrDefault(x => x != "password");

    public static bool HasSignInChoice =>
        Features.AuthProviderIds.Count > 1 || Features.AuthProviderIds.FirstOrDefault() == "password";

    public static bool HasPasswordSignIn => Features.AuthProviderIds.Contains("password");
}
