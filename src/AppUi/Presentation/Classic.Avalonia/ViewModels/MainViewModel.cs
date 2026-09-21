using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.App;
using VpnHood.AppUi.Common;
using VpnHood.AppUi.Hosting.Avalonia;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Resources;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Views;
using VpnHood.AppUi.Presentation.Classic.Avalonia.Views.Dialogs;
using VpnHood.AppLib.Api.Accounts;
using VpnHood.AppLib.Api.ClientProfiles;
using VpnHood.AppLib.Api.Sessions;
using VpnHood.AppLib.Api.Settings;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.ViewModels;

// What the home shows, read off the app through its API (VhApp) - the same values the web UI
// reads, shaped the way its home and servers pages shape them. Read again once a second, because
// the state's progress values and speeds move without an event and a paired browser has no other
// way to hear of a change, and after every action; always on the UI thread. The connect flows of
// the web UI's VpnHoodApp.ts and ConnectManager live here too, and the prompts its reloadState
// raises - the error dialog, the update notice, the review, the ad - go through the host.
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private const double Megabyte = 1_000_000;
    private static readonly TimeSpan NoticeLife = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromSeconds(299);
    private static readonly TimeSpan FifteenMinutes = TimeSpan.FromSeconds(899);

    private readonly DispatcherTimer _timer;
    private bool _isReloading;
    private bool _disposed;
    private DateTime _noticeUntil;
    private string? _shownErrorMessage;
    private DateTime? _ignoredSuppressTime;
    private bool _isUpdatePostponed;
    private bool _isReviewShown;
    private bool _isQuickLaunchPrompted;
    private bool _isInternalAdShown;

    public event PropertyChangedEventHandler? PropertyChanged;

    // the page host: where a prompt raised by a state change is shown
    public MainView? Host { get; set; }

    public string AppName => VhApp.Features.AppName;
    public string VersionText => $"v{VhApp.Features.Version.Build}";
    public string SettingsTitle => Strings.Current.Settings.ToUpperInvariant();
    public string AutoChipText => Strings.Current.Auto.ToUpperInvariant();
    public string SplitCountriesTitle => Strings.Current.SplitCountries.ToUpperInvariant();
    public string SplitAppsTitle => Strings.Current.SplitApps.ToUpperInvariant();
    public string ProtocolTitle => Strings.Current.ProtocolTitle.ToUpperInvariant();
    public string AccountTitle => Strings.Current.Account.ToUpperInvariant();
    public string CloakChipText => Strings.Current.Cloak.ToUpperInvariant();

    // Connect ships one server and lets the person pick a location in it; the client keeps a list of
    // servers, each with locations of its own. That one difference names the home row, fills it,
    // and decides whether the page behind it is a list of servers or of locations - the web UI's
    // isSingleProfileMode, everywhere it reads it.
    public bool IsSingleProfileMode => VhApp.IsSingleProfileMode;
    public string ServersRowTitle => (IsSingleProfileMode ? Strings.Current.Location : Strings.Current.Server).ToUpperInvariant();
    public string ServersPageTitle => IsSingleProfileMode ? Strings.Current.Location : Strings.Current.Servers;

    // A server is added by its access key, which only a client head takes (IsAddAccessKeySupported).
    // A remote cannot type a vh:// key, so on a TV the button says so and leads to the phone -
    // exactly what the web UI's servers page does with it.
    public bool CanAddServer => VhApp.Features.IsAddAccessKeySupported;
    public string AddServerText => IsTv ? Strings.Current.AddOrRemoveServers : Strings.Current.AddServer;
    public string AddServerGlyph => IsTv ? Mdi.Cellphone : Mdi.PlusCircle;

    // A TV hands everything but connecting to a phone (TV plan §3.1); the row that does so shows
    // only there. The app's word, not the UI's - unless this UI is the phone's, driving the TV.
    public bool IsTv => VhApp.IsTvUi;
    public bool IsNotTv => !IsTv;

    // the drawer's door, off the TV; the account row, on it
    public bool HasAccountRow => IsTv && VhApp.Features.IsAccountSupported;
    public bool HasSplitAppsRow => VhApp.Features.IsExcludeAppsSupported || VhApp.Features.IsIncludeAppsSupported;

    // A debug field that is set shows on the version chip, and opens the developer page on the
    // first tap rather than the fifth - the web UI's isDebugDataHasValue.
    public bool HasDebugData { get; private set => Set(ref field, value); }

    // The person's choice when there is one, the device's language otherwise - the pair
    // VpnHoodApp itself resolves at every settings change.
    private Task? _cultureSwitch;

    private static CultureInfo AppCulture => VhApp.UserSettings.CultureCode is { } code
        ? CultureInfo.GetCultureInfo(code)
        : CultureInfo.GetCultureInfo(VhApp.State.SystemUiCultureInfo.Code);

    // the connection, as the circle and the button show it
    public string Phase { get; private set => Set(ref field, value); } = "none";
    public string StateText { get; private set => Set(ref field, value); } = Strings.Current.Disconnected;
    public string StateGlyph { get; private set => Set(ref field, value); } = Mdi.PowerPlugOff;
    public string UsageText { get; private set => Set(ref field, value); } = "";
    public string ExpireText { get; private set => Set(ref field, value); } = "";
    public bool IsExpireWarning { get; private set => Set(ref field, value); }
    public double Progress { get; private set => Set(ref field, value); }
    public bool IsProgressVisible { get; private set => Set(ref field, value); }
    public bool IsConnected { get; private set => Set(ref field, value); }
    public bool IsPremiumSession { get; private set => Set(ref field, value); }
    public double SpeedsOpacity { get; private set => Set(ref field, value); }
    public string SpeedDown { get; private set => Set(ref field, value); } = "0.00";
    public string SpeedUp { get; private set => Set(ref field, value); } = "0.00";
    public string ConnectButtonText { get; private set => Set(ref field, value); } = Strings.Current.Connect;
    public bool IsConnectEnabled { get; private set => Set(ref field, value); } = true;
    public bool IsReconnectRequired { get; private set => Set(ref field, value); }

    // GoPremiumButton: the countdown of a timed session, "You are premium", or the pitch
    public bool ShowCountdown { get; private set => Set(ref field, value); }
    public string CountdownText { get; private set => Set(ref field, value); } = "";
    public string CountdownKind { get; private set => Set(ref field, value); } = "normal";
    public bool CanExtendByRewardedAd { get; private set => Set(ref field, value); }
    public bool ShowYouArePremium { get; private set => Set(ref field, value); }
    public bool ShowGoPremium { get; private set => Set(ref field, value); }

    // HomeBadge: the features in use right now
    public IReadOnlyList<FeatureBadge> Badges { get; private set => Set(ref field, value); } = [];
    public bool HasBadges { get; private set => Set(ref field, value); }

    // the home row and the page behind it: the location in connect, the server in the client
    public string ServersRowValue { get; private set => Set(ref field, value); } = Strings.Current.NoLocationSelected;
    public string? LocationFlagPath { get; private set => Set(ref field, value); }
    public bool HasLocationFlag { get; private set => Set(ref field, value); }
    public bool IsLocationAuto { get; private set => Set(ref field, value); } = true;
    public IReadOnlyList<LocationGroup> LocationGroups { get; private set => Set(ref field, value); } = [];
    public IReadOnlyList<ProfileItem> Profiles { get; private set => Set(ref field, value); } = [];

    // the other rows: the countries split, the apps split, the protocol, the account
    public string SplitCountryText { get; private set => Set(ref field, value); } = "";
    public bool ShowSplitCountryText { get; private set => Set(ref field, value); } = true;
    public IReadOnlyList<string> SplitCountryFlags { get; private set => Set(ref field, value); } = [];
    public bool HasSplitCountryFlags { get; private set => Set(ref field, value); }
    public string SplitAppsText { get; private set => Set(ref field, value); } = "";
    public string ProtocolText { get; private set => Set(ref field, value); } = "";
    public bool IsCloakOn { get; private set => Set(ref field, value); }
    public string AccountRowValue { get; private set => Set(ref field, value); } = "";

    // Nothing to show on the servers page but the way to add one: the web UI's NO_SERVER_AVAILABLE
    // warning, which it shows only where a key can be added at all.
    public bool HasNoServer { get; private set => Set(ref field, value); }
    public string NoServerText => Strings.Current.NoServerAvailable;

    // What the web UI says in a snackbar: a sentence about the tap just made, gone a few seconds
    // later. The two the servers row can produce are the reasons it does not open at all.
    public string NoticeText { get; private set => Set(ref field, value); } = "";
    public bool HasNotice { get; private set => Set(ref field, value); }

    public MainViewModel()
    {
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => _ = Reload());
        _timer.Start();
        Refresh();
    }

    // The state, read again from the app and shown: the clock's beat, and every action's last step.
    // One read at a time - over HTTP a read can outlast the beat - and a read that fails is logged
    // and tried again at the next beat, as the web UI's reloadState is.
    public async Task Reload()
    {
        if (_disposed || _isReloading)
            return;

        _isReloading = true;
        try {
            await VhApp.ReloadState(CancellationToken.None);
            if (!_disposed)
                Refresh();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not read the app's state.");
        }
        finally {
            _isReloading = false;
        }
    }

    // The configuration - the features, the settings, the profiles - read again after an action
    // that moved it, then shown.
    public async Task ReloadInfo()
    {
        await VhApp.ReloadInfo(CancellationToken.None);
        Refresh();
    }

    public void Refresh()
    {
        if (_disposed)
            return;

        // The language, asked of the app rather than of this thread: VpnHoodApp writes
        // CurrentUICulture on the thread that initializes it - here, the UI thread - so a language
        // chosen later, from the paired phone, would never reach a thread that already has its own.
        // The words come through the store's provider, which may be a web server, so a language the
        // app switched to is fetched first, and the page refreshed again when the words are here;
        // in process that is at once.
        if (Strings.Current.CultureName != AppCulture.Name && _cultureSwitch?.IsCompleted != false)
            _cultureSwitch = SwitchCulture(AppCulture);

        Refresh(cultureChanged: false);
    }

    private async Task SwitchCulture(CultureInfo culture)
    {
        try {
            if (await Strings.Current.SetCultureAsync(culture, CancellationToken.None) && !_disposed)
                Refresh(cultureChanged: true);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not load the words of the language. Culture: {Culture}", culture.Name);
        }
    }

    // Everything the pages show, read again; with the words too, when the language changed.
    private void Refresh(bool cultureChanged)
    {
        if (cultureChanged)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)); // the titles

        var state = VhApp.State;
        var profile = VhApp.CurrentClientProfileInfo;
        var connectionState = state.ConnectionState;

        // HomeConnectionInfo
        IsConnected = VhApp.IsConnected(state);
        IsPremiumSession = (VhApp.IsPremiumSupported && VhApp.IsPremiumUser) ||
                           (state.SessionInfo?.IsPremiumSession == true && IsConnected);
        Phase = connectionState switch {
            AppConnectionState.None => "none",
            AppConnectionState.Connected => "connected",
            AppConnectionState.Unstable => "unstable",
            AppConnectionState.Waiting => "waiting",
            AppConnectionState.Disconnecting => "disconnecting",
            _ => "connecting"
        };
        StateText = connectionState switch {
            AppConnectionState.None => Strings.Current.Disconnected,
            AppConnectionState.Initializing => Strings.Current.Initializing,
            AppConnectionState.Waiting => Strings.Current.Waiting,
            AppConnectionState.Diagnosing => Strings.Current.Diagnosing,
            AppConnectionState.ValidatingProxies => Strings.Current.ValidatingProxies,
            AppConnectionState.Connecting => Strings.Current.Connecting,
            AppConnectionState.Connected => Strings.Current.Connected,
            AppConnectionState.Disconnecting => Strings.Current.Disconnecting,
            AppConnectionState.WaitingForAd => Strings.Current.LoadingAd,
            AppConnectionState.FindingReachableServer => Strings.Current.FindingNetwork,
            AppConnectionState.FindingBestServer => Strings.Current.FindingBestServer,
            AppConnectionState.Unstable => Strings.Current.Unstable,
            _ => connectionState.ToString()
        };
        UsageText = IsConnected ? BandwidthUsage(state) : "";
        StateGlyph = connectionState switch {
            AppConnectionState.Connected => !state.CanDiagnose ? Mdi.Stethoscope : UsageText.Length > 0 ? "" : Mdi.Check,
            AppConnectionState.None => Mdi.PowerPlugOff,
            AppConnectionState.Waiting => Mdi.TimerSand,
            _ => ""
        };
        IsProgressVisible = state.StateProgress.HasValue;
        Progress = state.StateProgress ?? 0;
        RefreshExpiry(state);

        // ConnectionInfo
        SpeedsOpacity = IsConnected ? 1 : 0;
        SpeedDown = FormatSpeed(state.SessionStatus?.Speed.Received ?? 0);
        SpeedUp = FormatSpeed(state.SessionStatus?.Speed.Sent ?? 0);

        // #connectBtn
        ConnectButtonText = state.IsDiagnosing
            ? Strings.Current.StopDiagnosing
            : connectionState switch {
                AppConnectionState.Initializing => Strings.Current.Cancel,
                AppConnectionState.Disconnecting => Strings.Current.Disconnecting,
                AppConnectionState.Diagnosing => Strings.Current.StopDiagnosing,
                AppConnectionState.None => Strings.Current.Connect,
                _ => Strings.Current.Disconnect
            };
        IsConnectEnabled = connectionState == AppConnectionState.None || state.CanDisconnect;
        IsReconnectRequired = state.IsReconnectRequired && IsConnected;

        HasDebugData = VhApp.UserSettings.DebugData1 != null || VhApp.UserSettings.DebugData2 != null;

        RefreshPremiumButton(state);
        RefreshBadges(state);

        // ServersButton: the location in connect, the name of the chosen server in the client
        var location = state.ServerLocationInfo;
        ServersRowValue = IsSingleProfileMode
            ? location == null ? Strings.Current.NoLocationSelected
            : location.IsAuto ? Strings.Current.AutoSelect
            : LocationDisplay(location.TranslatedCountryName, location is { HasRegion: true, HasMultipleRegions: true } ? location.RegionName : null)
            : profile?.ClientProfileName ?? Strings.Current.NoServerSelected;
        IsLocationAuto = location == null || location.IsAuto;
        HasLocationFlag = !IsLocationAuto;
        LocationFlagPath = HasLocationFlag ? AppAssets.FlagPath(location?.CountryCode) : null;

        RefreshOtherRows(state);

        // the page behind that row, rebuilt only when a row differs: a new list every second would
        // rebuild the rows under the remote, and the focus with them
        RefreshLocations(profile, cultureChanged);
        RefreshProfiles(cultureChanged);

        if (HasNotice && DateTime.UtcNow > _noticeUntil) {
            HasNotice = false;
            NoticeText = "";
        }

        RefreshPrompts(state);
    }

    // the access key's expiry, under the state (HomeConnectionInfo.getExpireDate): not for a
    // connect build without premium, and only while connected
    private void RefreshExpiry(AppState state)
    {
        var expiration = state.SessionInfo?.AccessInfo?.ExpirationTime;
        if ((!VhApp.IsPremiumUser && !VhApp.IsPremiumSupported) || !IsConnected || expiration == null) {
            ExpireText = "";
            return;
        }
        ExpireText = $"{Strings.Current.Expire}: {Format.ShortDate(expiration.Value)}";
        IsExpireWarning = (expiration.Value - DateTime.UtcNow).TotalDays <= 3;
    }

    // GoPremiumButton.vue: the countdown of a trial or a rewarded session, else the premium mark,
    // else the pitch when the profile can be upgraded
    private void RefreshPremiumButton(AppState state)
    {
        var expiration = state.SessionStatus?.SessionExpirationTime;
        ShowCountdown = !VhApp.IsPremiumUser && expiration != null && IsConnected;
        if (ShowCountdown && expiration != null) {
            var remaining = expiration.Value - DateTime.UtcNow;
            CountdownText = Format.Countdown(remaining);
            CountdownKind = remaining < FiveMinutes ? "warning" : remaining < FifteenMinutes ? "alert" : "normal";
            CanExtendByRewardedAd = state.SessionStatus?.CanExtendByRewardedAd == true;
        }
        ShowYouArePremium = !ShowCountdown && VhApp.IsPremiumSupported && VhApp.IsPremiumUser;
        ShowGoPremium = !ShowCountdown && !ShowYouArePremium && VhApp.IsPremiumSupported && state.ClientProfile?.CanGoPremium == true;
    }

    // HomeBadge: one badge per feature in use (FeatureIcons.ts)
    private void RefreshBadges(AppState state)
    {
        var badges = new List<FeatureBadge>();
        if (state.SplitTunnelingState.IsSplittingTraffic)
            badges.Add(new FeatureBadge(Mdi.CallSplit, Mdi.Web, Strings.Current.SplitTunneling, FeaturePage.SplitTunneling));
        if (VhApp.IsCustomEndpointActive(state))
            badges.Add(new FeatureBadge(Mdi.IpNetwork, null, Strings.Current.CustomEndpoint, FeaturePage.Servers));
        if (VhApp.IsDnsCustomized(state))
            badges.Add(new FeatureBadge(Mdi.Dns, null, Strings.Current.Dns, FeaturePage.Dns));
        if (state.IsProxyEndPointActive)
            badges.Add(new FeatureBadge(Mdi.Diversify, null, Strings.Current.Proxies, FeaturePage.Proxies));

        if (badges.Count != Badges.Count || badges.Where((x, i) => x != Badges[i]).Any())
            Badges = badges;
        HasBadges = badges.Count > 0;
    }

    // SplitCountryButton, the apps row, the protocol row and the account row
    private void RefreshOtherRows(AppState state)
    {
        var split = state.SplitTunnelingState;
        var excluded = split.Countries;
        var isExcludeList = split.CountryMode == SplitCountryMode.ExcludeList;
        var allowMultipleFlags = isExcludeList && excluded.Count is > 0 and < 3;
        var showMyFlag = split.CountryMode == SplitCountryMode.ExcludeMyCountry;
        SplitCountryText = AppText.SplitCountryStatusText(state);
        ShowSplitCountryText = !allowMultipleFlags || excluded.Count < 3;
        var flagCodes = allowMultipleFlags ? excluded.ToArray()
            : showMyFlag && state.ClientCountryInfo != null ? [state.ClientCountryInfo.CountryCode]
            : [];
        if (flagCodes.Length != SplitCountryFlags.Count || flagCodes.Length > 0 && !ReferenceEquals(_flagCodes, null) && !flagCodes.SequenceEqual(_flagCodes))
            SplitCountryFlags = [.. flagCodes.Select(AppAssets.FlagPath).OfType<string>()];
        _flagCodes = flagCodes;
        HasSplitCountryFlags = SplitCountryFlags.Count > 0;

        SplitAppsText = AppText.SplitAppsStatusText();
        ProtocolText = AppText.ProtocolTitle(VhApp.ActiveProtocol(state));
        IsCloakOn = VhApp.UserSettings.UseTcpProxy;
        AccountRowValue = VhApp.Account?.Email ?? Strings.Current.SignIn;
    }

    private string[]? _flagCodes;

    // The prompts the web UI's reloadState raises off the state: the last error as a dialog, the
    // internal ad and the quick launch page, the update notice, the 'suppressed to' notice and the
    // review. Each once per occasion, so a poll never nags.
    private void RefreshPrompts(AppState state)
    {
        var host = Host;
        if (host == null)
            return;

        if (state.LastError != null && state.LastError.Message != _shownErrorMessage) {
            _shownErrorMessage = state.LastError.Message;
            _ = host.ShowErrorMessage(ErrorMessages.For(state.LastError, AppErrors.Context));
        }
        else if (state.LastError == null) {
            _shownErrorMessage = null;
        }

        if (state.IsWaitingForInternalAd == true) {
            if (!_isInternalAdShown) {
                _isInternalAdShown = true;
                host.Replace(new InternalAdView(host));
            }
        }
        else {
            _isInternalAdShown = false;
            if (state.IsQuickLaunchRecommended && !_isQuickLaunchPrompted) {
                _isQuickLaunchPrompted = true;
                host.Navigate(FeaturePages.QuickLaunch(host));
            }
        }

        if (state.UpdaterStatus?.Prompt == true && !_isUpdatePostponed && !host.IsUpdateNoticeShown)
            host.ShowUpdateNotice(state.UpdaterStatus);

        if (IsConnected && state.SessionInfo?.SuppressedTo == SessionSuppressType.Other &&
            _ignoredSuppressTime != state.ConnectRequestTime && !host.IsSnackbarShown)
            host.ShowSnackbar(Strings.Current.SessionSuppressedToOther, SnackbarKind.Suppress, hasTimer: false, hasClose: true);

        if (state.UserReviewRecommended != 0 && !_isReviewShown) {
            _isReviewShown = true;
            _ = host.ShowDialog(new UserReviewDialog(host, state.UserReviewRecommended));
        }
    }

    public void IgnoreSuppressNotice()
    {
        _ignoredSuppressTime = VhApp.State.ConnectRequestTime;
    }

    public void PostponeUpdate()
    {
        _isUpdatePostponed = true;
        VhApp.Api.App.VersionCheckPostpone(CancellationToken.None).Forget("Could not postpone the update notice.");
    }

    public void ReviewShown()
    {
        _isReviewShown = false;
    }

    private void RefreshLocations(ClientProfileInfo? profile, bool cultureChanged)
    {
        // the client's locations live in its servers' cards, one list per server
        var groups = IsSingleProfileMode ? BuildGroups(profile, isNested: false, isActiveProfile: true) : [];
        if (!cultureChanged && groups.Count == LocationGroups.Count &&
            !groups.Where((x, i) => !x.SameAs(LocationGroups[i])).Any())
            return;

        foreach (var group in groups) {
            var previous = LocationGroups.FirstOrDefault(x => x.Title == group.Title);
            if (previous != null)
                group.IsExpanded = previous.IsExpanded;
        }
        LocationGroups = groups;
    }

    private void RefreshProfiles(bool cultureChanged)
    {
        IReadOnlyList<ClientProfileInfo> infos = IsSingleProfileMode ? [] : ProfileInfos();
        HasNoServer = !IsSingleProfileMode && CanAddServer && infos.Count == 0;
        var profiles = BuildProfiles(infos);
        if (!cultureChanged && profiles.Count == Profiles.Count &&
            !profiles.Where((x, i) => !x.SameAs(Profiles[i])).Any())
            return;

        foreach (var item in profiles) {
            var previous = Profiles.FirstOrDefault(x => x.ClientProfileId == item.ClientProfileId);
            if (previous != null)
                item.IsExpanded = previous.IsExpanded;
        }
        Profiles = profiles;
    }

    public IReadOnlyList<ClientProfileInfo> ProfileInfos()
    {
        return VhApp.ClientProfileInfos;
    }

    // The web UI's ExpansionPanel: every server the app holds, the one it is set to marked, each
    // opened when it is that one or has a single location - nothing to open.
    private static IReadOnlyList<ProfileItem> BuildProfiles(IReadOnlyList<ClientProfileInfo> infos)
    {
        var currentId = VhApp.CurrentClientProfileInfo?.ClientProfileId;
        // ReSharper disable once UseCollectionExpression
        return infos
            .Select(x => {
                var isSingleLocation = x.LocationInfos.Count < 2;
                var isActive = x.ClientProfileId == currentId;
                return new ProfileItem {
                    ClientProfileId = x.ClientProfileId,
                    Name = x.ClientProfileName,
                    IsActive = isActive,
                    IsSingleLocation = isSingleLocation,
                    IsBuiltIn = x.IsBuiltIn,
                    SupportIdText = $"SID:{x.SupportId}",
                    HostName = x.HostNames.FirstOrDefault() ?? "",
                    HasCustomEndpoint = x is { IsCustomServerEndpointsEnabled: true, CustomServerEndpoints.Length: > 0 },
                    CollapsedFlags = CollapsedFlags(x),
                    MoreLocationCount = Math.Max(0, LocationCount(x) - ProfileItem.CollapsedFlagCount),
                    Groups = BuildGroups(x, isNested: true, isActiveProfile: isActive),
                    IsExpanded = isActive || isSingleLocation
                };
            }).ToArray();
    }

    // ExpansionPanelCollapsed.vue: the first flags of a closed server, the fastest choice as the earth
    private static IReadOnlyList<CollapsedFlag> CollapsedFlags(ClientProfileInfo profile)
    {
        return [.. profile.LocationInfos
            .Take(ProfileItem.CollapsedFlagCount + 1)
            .Where(x => !x.IsNestedCountry)
            .Select(x => new CollapsedFlag(VhApp.IsLocationAutoSelected(x.CountryCode) ? null : x.CountryCode))];
    }

    // Util.calcLocationCount: the countries, without the automatic choice and the regions
    private static int LocationCount(ClientProfileInfo profile)
    {
        return profile.LocationInfos.Count(x => x.CountryCode != "*" && !x.IsNestedCountry);
    }

    // "United States (California)", "USA (California)" as the web UI abbreviates it
    private static string LocationDisplay(string countryName, string? regionName)
    {
        var text = regionName == null ? countryName : $"{countryName} ({regionName})";
        return text.Replace("United States (", "USA (");
    }

    // (bytes/s * 10) / 1e6 with two decimals - the web UI's formatSpeed
    private static string FormatSpeed(long bytesPerSecond)
    {
        return (bytesPerSecond * 10 / Megabyte).ToString("0.00", CultureInfo.InvariantCulture);
    }

    // "12MB of 1GB" for a session with a traffic cap; empty otherwise (the web UI's bandwidthUsage)
    private static string BandwidthUsage(AppState state)
    {
        var max = state.SessionInfo?.AccessInfo?.MaxTotalTraffic ?? 0;
        if (max <= 0 || state.SessionStatus == null)
            return "";
        var traffic = state.SessionStatus.SessionTraffic;
        return $"{Format.TrafficTight(traffic.Sent + traffic.Received)} {Strings.Current.Of} {Format.TrafficTight(max)}";
    }

    // The web UI's LocationList: Free and Premium cards when the profile has both and the person is
    // not premium; only the premium rows when the person is; one card of everything otherwise.
    private static IReadOnlyList<LocationGroup> BuildGroups(ClientProfileInfo? profile, bool isNested, bool isActiveProfile)
    {
        if (profile == null)
            return [];

        var isPremiumSupported = VhApp.Features.Premium != null;
        var isPremiumUser = profile.IsPremium;
        var all = profile.LocationInfos;
        var free = all.Where(x => x.Options.HasFree).ToArray();
        var premium = all.Where(x => x.Options.HasPremium).ToArray();
        var hasGroups = free.Length > 0 && premium.Length > 0;
        var showGroups = hasGroups && (!isPremiumSupported || !isPremiumUser);
        var selected = profile.SelectedLocationInfo?.ServerLocation;

        LocationGroup Group(string title, bool isPremiumGroup, IEnumerable<ServerLocationItem> locations)
        {
            return new LocationGroup {
                Title = title,
                IsPremium = isPremiumGroup,
                IsNested = isNested,
                Items = [.. locations.Select(x => new LocationItem(
                    ClientProfileId: profile.ClientProfileId,
                    ServerLocation: x.ServerLocation,
                    CountryCode: x.CountryCode,
                    Name: x.IsAuto ? Strings.Current.Fastest : x.IsNestedCountry ? x.RegionName : x.TranslatedCountryName,
                    IsNested: x.IsNestedCountry,
                    IsAuto: x.IsAuto,
                    // only under the server the app is set to: another server's own choice is not
                    // where this app is going (the web UI's isActiveItem, which returns false for
                    // any profile but the active one)
                    IsActive: isActiveProfile && x.ServerLocation == selected &&
                              profile.IsPremiumLocationSelected == isPremiumGroup,
                    IsPremiumGroup: isPremiumGroup,
                    HasUnblockable: x.Options.HasUnblockable && isPremiumGroup,
                    ShowCrown: isPremiumGroup && !isPremiumUser))]
            };
        }

        if (showGroups)
            return [Group(Strings.Current.FreeLocations, false, free), Group(Strings.Current.PremiumLocations, true, premium)];
        if (hasGroups)
            return [Group("", true, premium)];
        return [Group("", premium.Length > 0, all)];
    }

    // ---- connecting, as the web UI's VpnHoodApp.connect and ConnectManager do it ----

    private long _lastConnectPress = Environment.TickCount64 - 1000;

    // Connect, or disconnect: the one button's two meanings, decided by the app's own flags, as the
    // web UI's onConnectButtonClick decides them - with its guard against a double press.
    public async Task ToggleConnect()
    {
        if (_lastConnectPress >= Environment.TickCount64 - 1000)
            return;
        _lastConnectPress = Environment.TickCount64;

        var state = VhApp.State;
        if (state.CanDisconnect) {
            await Disconnect();
            return;
        }
        if (state.CanConnect)
            await ConnectWithCurrentProfile();
    }

    public async Task Disconnect()
    {
        try {
            await VhApp.Api.App.Disconnect(CancellationToken.None);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not disconnect.");
        }
        finally {
            await Reload();
        }
    }

    // ConnectManager.connectWithCurrentProfile: no server chosen yet opens the servers page
    public async Task ConnectWithCurrentProfile(bool isDiagnose = false)
    {
        if (VhApp.ClientProfileId is not { } profileId) {
            Host?.Navigate(new LocationsView(this, Host));
            return;
        }
        await ConnectWithProfile(profileId, isDiagnose);
    }

    // ConnectManager.connectWithProfile: the server's own choice of location and side, a premium
    // person always on the premium side of the automatic choice
    public async Task ConnectWithProfile(Guid clientProfileId, bool isDiagnose = false)
    {
        var info = VhApp.FindClientProfileInfo(clientProfileId);
        var selected = info?.SelectedLocationInfo;
        var serverLocation = selected?.ServerLocation;
        var isPremium = (info?.IsPremiumLocationSelected ?? false) || selected?.Options is { HasPremium: true, HasFree: false };
        if (selected?.Options is { HasPremium: false, HasFree: true }) isPremium = false;
        if (VhApp.IsPremiumUser && !isPremium) {
            isPremium = true;
            serverLocation = null;
        }

        await ConnectWith(new ConnectRequest(clientProfileId, serverLocation, isPremium, ConnectPlanId.Normal, isDiagnose));
    }

    // ConnectManager.connectWithLocation: the promote page first when the location asks to be
    // asked, and the connect only when it does not.
    public async Task ConnectWith(ConnectRequest request)
    {
        if (request is { ServerLocation: not null, IsDiagnose: false } && ShowPromoteIfNeeded(request))
            return;

        await Connect(request);
    }

    // VpnHoodApp.connect: the connect itself, with nobody asked anything, and the profile's choice
    // written and saved. The promote page calls THIS and not ConnectWith, exactly as the web UI's
    // promote page calls vhApp.connect: the person standing on that page has just answered the
    // question, so asking it again would put the same page in front of them for ever.
    public async Task Connect(ConnectRequest request)
    {
        if (request.GoToHome)
            Host?.GoHome();

        try {
            var api = VhApp.Api;
            var connect = request.IsDiagnose
                ? api.App.Diagnose(request.ClientProfileId, request.ServerLocation, request.PlanId, CancellationToken.None)
                : api.App.Connect(request.ClientProfileId, request.ServerLocation, request.PlanId, CancellationToken.None);

            // the profile's choice, as the web UI writes it right after asking for the connect
            await api.ClientProfiles.Update(request.ClientProfileId, new ClientProfileUpdateParams {
                IsPremiumLocationSelected = new Patch<bool>(request.IsPremium),
                SelectedLocation = new Patch<string?>(request.ServerLocation)
            }, CancellationToken.None);
            var settings = VhApp.UserSettings;
            settings.ClientProfileId = request.ClientProfileId;
            await VhApp.SaveUserSettings(settings, CancellationToken.None);

            await connect;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not connect.");
        }
        finally {
            await Reload();
        }
    }

    // ConnectManager.showPromoteDialog: a location whose policy wants the person asked - an ad, a
    // trial, a purchase - gets the promote page instead of a connect
    private bool ShowPromoteIfNeeded(ConnectRequest request)
    {
        var host = Host;
        if (host == null)
            return false;
        var info = VhApp.FindClientProfileInfo(request.ClientProfileId);
        var options = info?.LocationInfos.FirstOrDefault(x => x.ServerLocation == request.ServerLocation)?.Options;
        if (options?.Prompt != true)
            return false;
        host.Navigate(new PromoteView(host, request.ClientProfileId, request.ServerLocation ?? "", request.IsPremium));
        return true;
    }

    // a location chosen in the list (LocationListItem.internalConnect)
    public async Task ConnectTo(LocationItem location)
    {
        await ConnectWith(new ConnectRequest(location.ClientProfileId, location.ServerLocation, location.IsPremiumGroup, ConnectPlanId.Normal));
    }

    // a server chosen in the list, or one just added by its key
    public async Task ConnectToProfile(Guid clientProfileId)
    {
        await ConnectWithProfile(clientProfileId);
    }

    public async Task Diagnose()
    {
        try {
            await VhApp.Api.App.Diagnose(VhApp.UserSettings.ClientProfileId, null, ConnectPlanId.Normal, CancellationToken.None);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The diagnosis failed.");
        }
        finally {
            await Reload();
        }
    }

    // ReconnectRequiredAlert: a fresh session on the same server
    public async Task Reconnect()
    {
        await Disconnect();
        await ConnectWithCurrentProfile();
    }

    public async Task ClearReconnectRequired()
    {
        await VhApp.Api.App.ClearReconnectRequired(CancellationToken.None);
        IsReconnectRequired = false;
    }

    // A server the person typed the key of. The key is the app's to judge: an unreadable one throws,
    // and the page that called this says so (INVALID_ACCESS_KEY_FORMAT), as the web UI's dialog does.
    public async Task<Guid> AddAccessKey(string accessKey)
    {
        var profile = await VhApp.Api.ClientProfiles.AddByAccessKey(accessKey, CancellationToken.None);
        await ReloadInfo();
        return profile.ClientProfileId;
    }

    // Why the servers row leads nowhere, when it does: a head that takes no keys can hold nothing
    // to choose between. The web UI's buttonClickHandler, which says this and stays home.
    public string? ServersUnreachableReason()
    {
        if (CanAddServer)
            return null;

        var profiles = VhApp.ClientProfileInfos;
        if (profiles.Count == 0)
            return Strings.Current.NoClientProfileAvailable;

        return profiles is [{ LocationInfos.Count: < 2 }]
            ? Strings.Current.NoAdditionalLocationAvailable
            : null;
    }

    public void ShowNotice(string text)
    {
        NoticeText = text;
        HasNotice = true;
        _noticeUntil = DateTime.UtcNow + NoticeLife;
    }

    // ---- the account, as the web UI's VpnHoodApp.signIn and friends ----

    // The store's sign-in. Its sheet is the device's own; the loading dialog covers the wait.
    // Cancelled silently, unless a purchase was waiting on it.
    public async Task SignIn(bool onPurchase = false)
    {
        var host = Host ?? throw new InvalidOperationException("The main view is not attached.");
        if (!VhApp.Features.IsAccountSupported)
            throw new InvalidOperationException("Account service is not available.");
        var providerId = VhApp.PrimaryProviderId ?? throw new InvalidOperationException("This build reports no sign-in method.");

        using var loading = host.Loading();
        try {
            await VhApp.Api.Account.SignIn(new SignInOptions { ProviderId = providerId }, CancellationToken.None);
            await AfterSignedIn(onPurchase);
        }
        catch (Exception ex) {
            // the failure's own name, whether it was thrown here or reported over the API
            var name = ex.ToApiError().TypeName;
            if (name == nameof(TaskCanceledException) || name == nameof(OperationCanceledException)) {
                if (onPurchase)
                    throw new Exception(Strings.Current.SignInCanceledByUser);
                return;
            }
            if (name == "AuthenticationException")
                throw new Exception(Strings.Current.SignInFailedMsg);
            if (name == nameof(HttpRequestException) && ex.Message.Contains("400"))
                throw new Exception(Strings.Current.LoginConnectionErrorMsg);
            throw;
        }
    }

    // the portal's own email and password; a second factor comes back as a challenge
    public async Task<SignInResult> SignInWithPassword(string email, string password)
    {
        var result = await VhApp.Api.Account.SignIn(
            new SignInOptions { ProviderId = "password", UserName = email, Password = password }, CancellationToken.None);
        if (result.State == SignInState.SignedIn)
            await AfterSignedIn(false);
        return result;
    }

    public async Task<SignInResult> CompleteSignInChallenge(string code)
    {
        var result = await VhApp.Api.Account.SignIn(
            new SignInOptions { ProviderId = "password", TwoFactorCode = code }, CancellationToken.None);
        if (result.State == SignInState.SignedIn)
            await AfterSignedIn(false);
        return result;
    }

    private async Task AfterSignedIn(bool onPurchase)
    {
        await VhApp.LoadAccount(false, CancellationToken.None);
        await ReloadInfo();
        // sign-in is otherwise silent; a purchase confirms itself
        if (!onPurchase && VhApp.Account?.Email is { } email)
            Host?.ShowSnackbar(Strings.Current.SignedInAsX(email));
    }

    public async Task SignOut()
    {
        var host = Host ?? throw new InvalidOperationException("The main view is not attached.");
        if (!await host.Confirm(Strings.Current.ConfirmSignOutTitle, Strings.Current.ConfirmSignOutDesc))
            return;

        using var loading = host.Loading();
        await VhApp.Api.Account.SignOut(CancellationToken.None);
        await VhApp.LoadAccount(false, CancellationToken.None);
        await ReloadInfo();
        host.GoHome();
    }

    // Permanent, and confirmed by the caller; the tunnel goes with a premium the account paid for.
    public async Task DeleteAccount()
    {
        var host = Host ?? throw new InvalidOperationException("The main view is not attached.");
        using var loading = host.Loading();
        await VhApp.Api.Account.Delete(CancellationToken.None);
        if (IsConnected)
            await VhApp.Api.App.Disconnect(CancellationToken.None);
        await VhApp.LoadAccount(false, CancellationToken.None);
        await ReloadInfo();
        host.GoHome();
    }

    // signed out only (keyring plan §7): the device's copy is the only one there is
    public async Task RemovePremiumCode()
    {
        var host = Host ?? throw new InvalidOperationException("The main view is not attached.");
        var profile = VhApp.State.ClientProfile ?? throw new InvalidOperationException("Could not find the profile in the state for remove premium code.");
        if (!profile.HasAccessCode)
            throw new InvalidOperationException("The profile does not have a premium code.");

        using var loading = host.Loading();
        if (IsConnected)
            await VhApp.Api.App.Disconnect(CancellationToken.None);
        await VhApp.Api.ClientProfiles.Update(profile.ClientProfileId, new ClientProfileUpdateParams {
            AccessCode = new Patch<string?>(null)
        }, CancellationToken.None);
        await ReloadInfo();
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
    }
}
