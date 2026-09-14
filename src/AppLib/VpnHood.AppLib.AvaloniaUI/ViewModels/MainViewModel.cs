using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.AvaloniaUI.Resources;
using VpnHood.AppLib.ClientProfiles;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.AppLib.AvaloniaUI.ViewModels;

// What the UI shows, read off VpnHoodApp in process - no API layer, no JSON: the same objects the
// web UI reads over HTTP, shaped the way its home and servers pages shape them. Refreshed on the
// app's own events and once a second, because the state's progress values and speeds move without
// an event, and always on the UI thread.
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private const double Megabyte = 1_000_000;
    private const double Gigabyte = 1000 * Megabyte;
    private static readonly TimeSpan NoticeLife = TimeSpan.FromSeconds(6);

    private readonly VpnHoodApp _app = VpnHoodApp.Instance;
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AppName => _app.Resources.Strings.AppName;
    public string VersionText => $"{Strings.Current.AbbreviationVersion} {_app.Features.Version.Build}";
    public string SettingsTitle => Strings.Current.Settings.ToUpperInvariant();
    public string AutoChipText => Strings.Current.Auto.ToUpperInvariant();

    // Connect ships one server and lets the person pick a location in it; the client keeps a list of
    // servers, each with locations of its own. That one difference names the home row, fills it,
    // and decides whether the page behind it is a list of servers or of locations - the web UI's
    // isSingleProfileMode, everywhere it reads it.
    public bool IsSingleProfileMode => AppProduct.IsSingleProfileMode(_app.Features.UiName);
    public string ServersRowTitle => (IsSingleProfileMode ? Strings.Current.Location : Strings.Current.Server).ToUpperInvariant();
    public string ServersPageTitle => IsSingleProfileMode ? Strings.Current.Location : Strings.Current.Servers;

    // A server is added by its access key, which only a client head takes (IsAddAccessKeySupported).
    // A remote cannot type a vh:// key, so on a TV the button says so and leads to the phone -
    // exactly what the web UI's servers page does with it.
    public bool CanAddServer => _app.Features.IsAddAccessKeySupported;
    public string AddServerText => IsTv ? Strings.Current.AddOrRemoveServers : Strings.Current.AddServer;
    public string AddServerGlyph => IsTv ? Mdi.Cellphone : Mdi.PlusCircleOutline;

    // A TV hands everything but connecting to a phone (TV plan §3.1); the row that does so shows
    // only there. The app's word, not the UI's: the same views serve a phone.
    public bool IsTv => _app.Features.IsTv;

    // A debug field that is set shows on the version chip, and opens the developer page on the
    // first tap rather than the fifth - the web UI's isDebugDataHasValue.
    public bool HasDebugData { get; private set => Set(ref field, value); }

    // The person's choice when there is one, the device's language otherwise - the pair
    // VpnHoodApp itself resolves at every settings change.
    private CultureInfo AppCulture => _app.UserSettings.CultureCode is { } code
        ? CultureInfo.GetCultureInfo(code)
        : _app.SystemUiCulture;

    // the connection, as the circle and the button show it
    public string Phase { get; private set => Set(ref field, value); } = "none";
    public string StateText { get; private set => Set(ref field, value); } = Strings.Current.Disconnected;
    public string StateGlyph { get; private set => Set(ref field, value); } = Mdi.PowerPlugOff;
    public string UsageText { get; private set => Set(ref field, value); } = "";
    public double Progress { get; private set => Set(ref field, value); }
    public bool IsProgressVisible { get; private set => Set(ref field, value); }
    public bool IsConnected { get; private set => Set(ref field, value); }
    public double SpeedsOpacity { get; private set => Set(ref field, value); }
    public string SpeedDown { get; private set => Set(ref field, value); } = "0.00";
    public string SpeedUp { get; private set => Set(ref field, value); } = "0.00";
    public string ConnectButtonText { get; private set => Set(ref field, value); } = Strings.Current.Connect;
    public bool IsConnectEnabled { get; private set => Set(ref field, value); } = true;
    public string ErrorText { get; private set => Set(ref field, value); } = "";
    public bool HasError { get; private set => Set(ref field, value); }

    // the home row and the page behind it: the location in connect, the server in the client
    public string ServersRowValue { get; private set => Set(ref field, value); } = Strings.Current.NoLocationSelected;
    public Bitmap? LocationFlag { get; private set => Set(ref field, value); }
    public bool HasLocationFlag { get; private set => Set(ref field, value); }
    public bool IsLocationAuto { get; private set => Set(ref field, value); } = true;
    public IReadOnlyList<LocationGroup> LocationGroups { get; private set => Set(ref field, value); } = [];
    public IReadOnlyList<ProfileItem> Profiles { get; private set => Set(ref field, value); } = [];

    // Nothing to show on the servers page but the way to add one: the web UI's NO_SERVER_AVAILABLE
    // warning, which it shows only where a key can be added at all.
    public bool HasNoServer { get; private set => Set(ref field, value); }
    public string NoServerText => Strings.Current.NoServerAvailable;

    // What the web UI says in a snackbar: a sentence about the tap just made, gone a few seconds
    // later. The two the servers row can produce are the reasons it does not open at all.
    public string NoticeText { get; private set => Set(ref field, value); } = "";
    public bool HasNotice { get; private set => Set(ref field, value); }
    private DateTime _noticeUntil;

    public MainViewModel()
    {
        _app.ConnectionStateChanged += OnAppChanged;
        _app.UiHasChanged += OnAppChanged;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh());
        _timer.Start();
        Refresh();
    }

    private void OnAppChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(Refresh);
    }

    public void Refresh()
    {
        if (_disposed)
            return;

        // The language, asked of the app rather than of this thread: VpnHoodApp writes
        // CurrentUICulture on the thread that initializes it - here, the UI thread - so a language
        // chosen later, from the paired phone, would never reach a thread that already has its own.
        // The words change with everything else this reads, at the same beat.
        var cultureChanged = Strings.Current.SetCulture(AppCulture);
        if (cultureChanged)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)); // the titles

        var state = _app.State;
        var profile = _app.CurrentClientProfileInfo;
        var connectionState = state.ConnectionState;

        // HomeConnectionInfo
        IsConnected = connectionState == AppConnectionState.Connected;
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
            AppConnectionState.Connected => UsageText.Length > 0 ? "" : Mdi.Check,
            AppConnectionState.None => Mdi.PowerPlugOff,
            AppConnectionState.Waiting => Mdi.TimerSand,
            _ => ""
        };
        IsProgressVisible = state.StateProgress.HasValue;
        Progress = state.StateProgress ?? 0;

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
        ErrorText = state.LastError?.Message ?? "";
        HasError = ErrorText.Length > 0;

        HasDebugData = _app.UserSettings.DebugData1 != null || _app.UserSettings.DebugData2 != null;

        // ServersButton: the location in connect, the name of the chosen server in the client
        var location = state.ServerLocationInfo;
        ServersRowValue = IsSingleProfileMode
            ? location == null ? Strings.Current.NoLocationSelected
            : location.IsAuto ? Strings.Current.AutoSelect
            : LocationDisplay(location.TranslatedCountryName, location.HasRegion && location.HasMultipleRegions ? location.RegionName : null)
            : profile?.ClientProfileName ?? Strings.Current.NoServerSelected;
        IsLocationAuto = location == null || location.IsAuto;
        HasLocationFlag = !IsLocationAuto;
        LocationFlag = HasLocationFlag ? Flags.Get(location?.CountryCode) : null;

        // the page behind that row, rebuilt only when a row differs: a new list every second would
        // rebuild the rows under the remote, and the focus with them
        RefreshLocations(profile, cultureChanged);
        RefreshProfiles(cultureChanged);

        if (HasNotice && DateTime.UtcNow > _noticeUntil) {
            HasNotice = false;
            NoticeText = "";
        }
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

    private IReadOnlyList<ClientProfileInfo> ProfileInfos()
    {
        return _app.ClientProfileService.List().Select(x => x.ToInfo(_app.Features)).ToArray();
    }

    // The web UI's ExpansionPanel: every server the app holds, the one it is set to marked, each
    // opened when it is that one or has a single location - nothing to open.
    private IReadOnlyList<ProfileItem> BuildProfiles(IReadOnlyList<ClientProfileInfo> infos)
    {
        var currentId = _app.CurrentClientProfileInfo?.ClientProfileId;
        return infos.Select(x => {
            var isSingleLocation = x.LocationInfos.Length < 2;
            var isActive = x.ClientProfileId == currentId;
            return new ProfileItem {
                ClientProfileId = x.ClientProfileId,
                Name = x.ClientProfileName,
                IsActive = isActive,
                IsSingleLocation = isSingleLocation,
                SupportIdText = $"SID:{x.SupportId}",
                HostName = x.HostNames.FirstOrDefault() ?? "",
                Groups = BuildGroups(x, isNested: true, isActiveProfile: isActive),
                IsExpanded = isActive || isSingleLocation
            };
        }).ToArray();
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
        return $"{FormatTraffic(traffic.Sent + traffic.Received)} {Strings.Current.Of} {FormatTraffic(max)}";
    }

    private static string FormatTraffic(long bytes)
    {
        return bytes >= Gigabyte
            ? (bytes / Gigabyte).ToString("0.#", CultureInfo.InvariantCulture) + "GB"
            : (bytes / Megabyte).ToString("0", CultureInfo.InvariantCulture) + "MB";
    }

    // The web UI's LocationList: Free and Premium cards when the profile has both and the person is
    // not premium; only the premium rows when the person is; one card of everything otherwise.
    private IReadOnlyList<LocationGroup> BuildGroups(ClientProfileInfo? profile, bool isNested, bool isActiveProfile)
    {
        if (profile == null)
            return [];

        var isPremiumSupported = _app.Features.Premium != null;
        var isPremiumUser = profile.IsPremium;
        var all = profile.LocationInfos;
        var free = all.Where(x => x.Options.HasFree).ToArray();
        var premium = all.Where(x => x.Options.HasPremium).ToArray();
        var hasGroups = free.Length > 0 && premium.Length > 0;
        var showGroups = hasGroups && (!isPremiumSupported || !isPremiumUser);
        var selected = profile.SelectedLocationInfo?.ServerLocation;

        LocationGroup Group(string title, bool isPremiumGroup, IEnumerable<ClientServerLocationInfo> locations)
        {
            return new LocationGroup {
                Title = title,
                IsPremium = isPremiumGroup,
                IsNested = isNested,
                Items = locations.Select(x => new LocationItem(
                    ClientProfileId: profile.ClientProfileId,
                    ServerLocation: x.ServerLocation,
                    CountryCode: x.CountryCode,
                    Name: x.IsAuto ? Strings.Current.Fastest : x.IsNestedCountry ? x.RegionName : x.TranslatedCountryName,
                    IsNested: x.IsNestedCountry,
                    IsAuto: x.CountryCode == ServerLocationInfo.AutoCountryCode,
                    // only under the server the app is set to: another server's own choice is not
                    // where this app is going (the web UI's isActiveItem, which returns false for
                    // any profile but the active one)
                    IsActive: isActiveProfile && x.ServerLocation == selected &&
                              profile.IsPremiumLocationSelected == isPremiumGroup,
                    IsPremiumGroup: isPremiumGroup,
                    HasUnblockable: x.Options.HasUnblockable && isPremiumGroup,
                    ShowCrown: isPremiumGroup && !isPremiumUser)).ToArray()
            };
        }

        if (showGroups)
            return [Group(Strings.Current.FreeLocations, false, free), Group(Strings.Current.PremiumLocations, true, premium)];
        if (hasGroups)
            return [Group("", true, premium)];
        return [Group("", premium.Length > 0, all)];
    }

    // Connect, or disconnect: the one button's two meanings, decided by the app's own flags, as the
    // web UI's onConnectButtonClick decides them.
    public async Task ToggleConnect()
    {
        try {
            var state = _app.State;
            if (state.CanDisconnect)
                await _app.Disconnect();
            else if (state.CanConnect)
                await _app.Connect(new ConnectOptions { PlanId = PlanFor(_app.CurrentClientProfileInfo?.SelectedLocationInfo) });
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not connect or disconnect.");
        }
        finally {
            Dispatcher.UIThread.Post(Refresh);
        }
    }

    // A server chosen in the list, or one just added by its key: the app connects where that server
    // is already set to go, as the web UI's connectWithProfile does.
    public async Task ConnectToProfile(Guid clientProfileId)
    {
        try {
            var location = _app.ClientProfileService.FindInfo(clientProfileId)?.SelectedLocationInfo;
            await _app.Connect(new ConnectOptions {
                ClientProfileId = clientProfileId,
                ServerLocation = location?.ServerLocation,
                PlanId = PlanFor(location)
            });
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not connect to the chosen server.");
        }
        finally {
            Dispatcher.UIThread.Post(Refresh);
        }
    }

    // A server the person typed the key of. The key is the app's to judge: an unreadable one throws,
    // and the page that called this says so (INVALID_ACCESS_KEY_FORMAT), as the web UI's dialog does.
    public Guid AddAccessKey(string accessKey)
    {
        var profile = _app.ClientProfileService.ImportAccessKey(accessKey);
        Refresh();
        return profile.ClientProfileId;
    }

    // Why the servers row leads nowhere, when it does: a head that takes no keys can hold nothing
    // to choose between. The web UI's buttonClickHandler, which says this and stays home.
    public string? ServersUnreachableReason()
    {
        if (CanAddServer)
            return null;

        var profiles = _app.ClientProfileService.List();
        if (profiles.Length == 0)
            return Strings.Current.NoClientProfileAvailable;

        return profiles.Length == 1 && profiles[0].ToInfo(_app.Features).LocationInfos.Length < 2
            ? Strings.Current.NoAdditionalLocationAvailable
            : null;
    }

    public void ShowNotice(string text)
    {
        NoticeText = text;
        HasNotice = true;
        _noticeUntil = DateTime.UtcNow + NoticeLife;
    }

    public async Task ConnectTo(LocationItem location)
    {
        try {
            var info = _app.CurrentClientProfileInfo?.LocationInfos
                .FirstOrDefault(x => x.ServerLocation == location.ServerLocation);
            await _app.Connect(new ConnectOptions {
                ClientProfileId = location.ClientProfileId,
                ServerLocation = location.ServerLocation,
                PlanId = PlanFor(info)
            });
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not connect to the chosen location.");
        }
        finally {
            Dispatcher.UIThread.Post(Refresh);
        }
    }

    // The plan the location's policy allows this person. The web UI asks first, in its promote
    // dialog (an ad, a trial, a purchase); that dialog is not ported, so here a location with no
    // free plan takes its trial when it offers one, and the server's own refusal shows otherwise.
    private static ConnectPlanId PlanFor(ClientServerLocationInfo? location)
    {
        var options = location?.Options;
        if (options == null || options.Normal != null)
            return ConnectPlanId.Normal;
        return options.PremiumByTrial != null ? ConnectPlanId.PremiumByTrial : ConnectPlanId.Normal;
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
        _app.ConnectionStateChanged -= OnAppChanged;
        _app.UiHasChanged -= OnAppChanged;
    }
}
