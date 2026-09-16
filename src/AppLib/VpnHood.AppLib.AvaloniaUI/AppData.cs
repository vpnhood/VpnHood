using Avalonia;
using VpnHood.AppLib.Abstractions.Accounts;
using VpnHood.AppLib.Assets;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.AppLib.Settings;
using VpnHood.AppLib.WebServer.Api;
using VpnHood.Core.Common.Messaging;
using AppConfig = VpnHood.AppLib.WebServer.Api.AppData;

namespace VpnHood.AppLib.AvaloniaUI;

// What the pages read off the app, and the one way they reach it: the app's API (AppApi), the same
// six interfaces in process - the web server's controllers, no listener - and over HTTP, from a
// paired browser. The pages read plain values held here - the features, the state, the settings,
// the profiles - and never the app itself, so the same pages run on the device and in a browser.
// The values are read again through the API: the state every second by the home's clock and after
// every action, the rest when the state says the configuration moved (AppState.ConfigTime, the
// web UI's own signal) or a page saved. The web UI's VpnHoodAppData, question for question.
public static class AppData
{
    private static AppApi? _api;
    private static AppConfig? _config;
    private static AppState? _state;

    public static AppApi Api => _api ?? throw new InvalidOperationException(
        $"The UI has not been given the app's API. A head must call {nameof(AppData)}.{nameof(Init)} before the UI starts.");

    public static bool IsInit => _api != null;
    public static bool IsConfigured { get; private set; }

    // The head's first step, before Avalonia starts: the API, and the configuration read through
    // it - the features decide the theme, which is applied as the application initializes. In
    // process the read completes at once.
    public static async Task Init(AppApi api, CancellationToken cancellationToken)
    {
        _api = api;
        await ReloadConfig(cancellationToken);
    }

    // The head's second step, once the assets folder can be read (on Android, in the activity - the
    // application has started by then): the languages this UI has, declared to the app as the web
    // UI's configure call declares its own, so the app's language list and its best-culture choice
    // are made from the words that exist. The fonts are registered here when Avalonia is already up
    // (Android), else by the application as it initializes.
    public static async Task Configure(CancellationToken cancellationToken)
    {
        // the folder, which on Android is a copy this call makes (AndroidAppContent): here, where a
        // head chooses the moment, rather than under the first page that asks for a picture
        _ = AppContent.FolderPath;
        if (Application.Current != null)
            AppAssets.RegisterFonts();

        _config = await Api.App.Configure(new ConfigParams { AvailableCultures = [.. Strings.AvailableCultures] }, cancellationToken);
        _state = _config.State;
        IsConfigured = true;
    }

    private static AppConfig Config => _config ?? throw new InvalidOperationException(
        $"The app's configuration has not been read. {nameof(AppData)}.{nameof(Init)} reads it.");

    public static AppFeatures Features => Config.Features;
    public static DeviceIntentFeatures Intents => Config.IntentFeatures;
    public static UserSettings UserSettings => Config.UserSettings;
    public static IReadOnlyList<ClientProfileInfo> ClientProfileInfos => Config.ClientProfileInfos;
    public static IReadOnlyList<UiCultureInfo> AvailableCultureInfos => Config.AvailableCultureInfos;
    public static AppState State => _state ?? Config.State;

    // Whether this UI is the remote: served by the app to a browser on another device, which is
    // what the API says of the request that read the configuration (the web UI's isRemote). Never
    // on the device itself.
    public static bool IsRemote => Config.IsRemote;

    // The profile the app is set to, whole (its locations), off the last configuration read; the
    // state carries its base info.
    public static ClientProfileInfo? CurrentClientProfileInfo => FindClientProfileInfo(UserSettings.ClientProfileId);

    public static ClientProfileInfo? FindClientProfileInfo(Guid? clientProfileId)
    {
        return clientProfileId == null
            ? null
            : ClientProfileInfos.FirstOrDefault(x => x.ClientProfileId == clientProfileId);
    }

    public static async Task ReloadState(CancellationToken cancellationToken)
    {
        var state = await Api.App.GetState(cancellationToken);
        var configMoved = _state != null && state.ConfigTime != _state.ConfigTime;
        _state = state;
        if (configMoved)
            await ReloadConfig(cancellationToken);
    }

    public static async Task ReloadConfig(CancellationToken cancellationToken)
    {
        _config = await Api.App.GetConfig(cancellationToken);
        _state = _config.State;
    }

    // The settings, written as one and read again: the app applies them as it saves, and may have
    // corrected a value. The object is the caller's - the one it read here and changed - so a
    // configuration read in between (the clock's) cannot lose the change.
    public static async Task SaveUserSettings(UserSettings userSettings, CancellationToken cancellationToken)
    {
        await Api.App.SetUserSettings(userSettings, cancellationToken);
        await ReloadConfig(cancellationToken);
    }

    // The account, as the web UI keeps it in UserState: read once at start and after a sign-in,
    // sign-out or purchase, never polled (account.vue says why).
    public static Account? Account { get; private set; }

    public static async Task LoadAccount(bool withRefresh, CancellationToken cancellationToken)
    {
        if (!Features.IsAccountSupported) {
            Account = null;
            return;
        }

        if (withRefresh) {
            try {
                await Api.Account.Refresh(cancellationToken);
            }
            catch (Exception) {
                // best effort: the portal is often what is blocked, and the saved account still shows
            }
        }

        Account = await Api.Account.Get(cancellationToken);
    }

    // The TV layout: the device's word, unless this UI is the remote - the web UI's isTvUi, which
    // asks the same two questions. A phone driving a TV gets the phone's layout.
    public static bool IsTvUi => Features.IsTv && !IsRemote;
    public static bool IsConnectApp => AppProduct.IsConnect(Features.UiName);
    public static bool IsSingleProfileMode => AppProduct.IsSingleProfileMode(Features.UiName);

    public static bool IsConnected(AppState state)
    {
        return state.ConnectionState is AppConnectionState.Connected or AppConnectionState.Unstable;
    }

    public static bool IsConnected() => IsConnected(State);

    // A build with no premium tier is the full app: nothing sold, no crown, everything allowed.
    public static bool IsPremiumSupported => Features.Premium != null;
    public static bool IsPremiumUser => State.ClientProfile?.IsPremium == true;
    public static bool IsPremiumByAccount => IsPremiumUser && Account?.Subscription != null;
    public static bool IsPremiumByCode => IsPremiumUser && !IsPremiumByAccount;
    public static bool CanImportAccessCode => State.ClientProfile?.CanImportAccessCode == true;
    public static bool CanViewAccessCode => State.ClientProfile?.CanViewAccessCode == true;
    public static bool CanTryPremium => State.ClientProfile?.CanTryPremium == true;
    public static bool CanGoPremium => State.ClientProfile?.CanGoPremium == true;
    public static Guid? ClientProfileId => State.ClientProfile?.ClientProfileId ?? UserSettings.ClientProfileId;

    // What a failure's message depends on besides the failure (ErrorMessages.For): the standing of
    // the session and the profile, off the state as it is when the failure is shown.
    public static ErrorContext ErrorContext => new() {
        HasDiagnoseRequested = State.HasDiagnoseRequested,
        IsPremiumSupported = IsPremiumSupported,
        IsPremiumUser = IsPremiumUser,
        IsPremiumByAccount = IsPremiumByAccount,
        CanTryPremium = CanTryPremium,
        HasAccessCode = State.ClientProfile?.HasAccessCode == true,
        CanImportAccessCode = CanImportAccessCode
    };

    public static bool IsPremiumFeature(AppFeature feature)
    {
        return Features.Premium?.Features.Contains(feature) ?? false;
    }

    // VpnHoodApp.IsPremiumFeatureAllowed, from what the API gives: a build with no premium tier
    // allows everything, a feature the tier does not sell is everyone's, the rest is the profile's.
    public static bool IsPremiumFeatureAllowed(AppFeature feature)
    {
        if (Features.Premium == null)
            return true;

        if (!Features.Premium.Features.Contains(feature))
            return true;

        return IsPremiumUser;
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

    // VpnHoodApp.HasDebugCommand, from the settings: the commands are the words of DebugData1.
    public static bool HasDebugCommand(string command)
    {
        return UserSettings.DebugData1?
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(command, StringComparer.OrdinalIgnoreCase) == true;
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
