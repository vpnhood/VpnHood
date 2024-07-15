using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Ga4.Trackers;
using Ga4.Trackers.Ga4Tags;
using Microsoft.Extensions.Logging;
using VpnHood.Client.Abstractions;
using VpnHood.Client.App.ClientProfiles;
using VpnHood.Client.App.Exceptions;
using VpnHood.Client.App.Services;
using VpnHood.Client.App.Settings;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Exceptions;
using VpnHood.Client.Diagnosing;
using VpnHood.Common;
using VpnHood.Common.ApiClients;
using VpnHood.Common.Exceptions;
using VpnHood.Common.IpLocations;
using VpnHood.Common.Jobs;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Trackers;
using VpnHood.Common.Utils;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Client.App;

public class VpnHoodApp : Singleton<VpnHoodApp>,
    IAsyncDisposable, IIpRangeProvider, IAdProvider, IJob
{
    private const string FileNameLog = "log.txt";
    private const string FileNameSettings = "settings.json";
    private const string FileNamePersistState = "state.json";
    private const string FolderNameProfiles = "profiles";
    private readonly SocketFactory? _socketFactory;
    private readonly bool _useInternalLocationService;
    private readonly bool _useExternalLocationService;
    private readonly string? _appGa4MeasurementId;
    private bool _hasConnectRequested;
    private bool _hasDisconnectRequested;
    private bool _hasDiagnoseStarted;
    private bool _hasDisconnectedByUser;
    private Guid? _activeClientProfileId;
    private string? _activeServerLocation;
    private DateTime? _connectRequestTime;
    private IpGroupManager? _ipGroupManager;
    private bool _isConnecting;
    private bool _isDisconnecting;
    private SessionStatus? _lastSessionStatus;
    private AppConnectionState _lastConnectionState;
    private bool _isLoadingIpGroup;
    private bool _isFindingCountryCode;
    private readonly TimeSpan _versionCheckInterval;
    private readonly AppPersistState _appPersistState;
    private readonly TimeSpan _reconnectTimeout;
    private readonly TimeSpan _autoWaitTimeout;
    private readonly TimeSpan _serverQueryTimeout;
    private CancellationTokenSource? _connectCts;
    private ClientProfile? _currentClientProfile;
    private VersionCheckResult? _versionCheckResult;
    private VpnHoodClient? _client;
    private readonly bool _logVerbose;
    private readonly bool? _logAnonymous;
    private UserSettings _oldUserSettings;
    private readonly bool _autoDiagnose;
    private readonly AppInternalAdService? _internalAdService;
    private readonly bool _allowEndPointTracker;
    private SessionStatus? LastSessionStatus => _client?.SessionStatus ?? _lastSessionStatus;
    private string VersionCheckFilePath => Path.Combine(StorageFolderPath, "version.json");
    public string TempFolderPath => Path.Combine(StorageFolderPath, "Temp");
    public event EventHandler? ConnectionStateChanged;
    public event EventHandler? UiHasChanged;
    public bool IsIdle => ConnectionState == AppConnectionState.None;
    public TimeSpan SessionTimeout { get; set; }
    public Diagnoser Diagnoser { get; set; } = new();
    public string StorageFolderPath { get; }
    public AppSettings Settings { get; }
    public UserSettings UserSettings => Settings.UserSettings;
    public AppFeatures Features { get; }
    public ClientProfileService ClientProfileService { get; }
    public IDevice Device { get; }
    public JobSection JobSection { get; }
    public TimeSpan TcpTimeout { get; set; } = ClientOptions.Default.ConnectTimeout;
    public AppLogService LogService { get; }
    public AppResource Resource { get; }
    public AppServices Services { get; }
    public DateTime? ConnectedTime { get; private set; }

    private VpnHoodApp(IDevice device, AppOptions? options = default)
    {
        options ??= new AppOptions();
        device.StartedAsService += DeviceOnStartedAsService;
        Directory.CreateDirectory(options.StorageFolderPath); //make sure directory exists
        Resource = options.Resource;

        Device = device;
        StorageFolderPath = options.StorageFolderPath ?? throw new ArgumentNullException(nameof(options.StorageFolderPath));
        Settings = AppSettings.Load(Path.Combine(StorageFolderPath, FileNameSettings));
        Settings.BeforeSave += SettingsBeforeSave;
        ClientProfileService = new ClientProfileService(Path.Combine(StorageFolderPath, FolderNameProfiles));
        SessionTimeout = options.SessionTimeout;
        _oldUserSettings = VhUtil.JsonClone(UserSettings);
        _socketFactory = options.SocketFactory;
        _useInternalLocationService = options.UseInternalLocationService;
        _useExternalLocationService = options.UseExternalLocationService;
        _appGa4MeasurementId = options.AppGa4MeasurementId;
        _versionCheckInterval = options.VersionCheckInterval;
        _reconnectTimeout = options.ReconnectTimeout;
        _autoWaitTimeout = options.AutoWaitTimeout;
        _appPersistState = AppPersistState.Load(Path.Combine(StorageFolderPath, FileNamePersistState));
        _versionCheckResult = VhUtil.JsonDeserializeFile<VersionCheckResult>(VersionCheckFilePath);
        _logVerbose = options.LogVerbose;
        _logAnonymous = options.LogAnonymous;
        _autoDiagnose = options.AutoDiagnose;
        _serverQueryTimeout = options.ServerQueryTimeout;
        _internalAdService = options.AdServices.Length > 0 ? new AppInternalAdService(options.AdServices, options.AdOptions) : null;
        _allowEndPointTracker = options.AllowEndPointTracker;
        Diagnoser.StateChanged += (_, _) => FireConnectionStateChanged();
        LogService = new AppLogService(Path.Combine(StorageFolderPath, FileNameLog), options.SingleLineConsoleLog);
        ActiveUiContext.OnChanged += ActiveUiContext_OnChanged;

        // configure update job section
        JobSection = new JobSection(new JobOptions
        {
            Interval = options.VersionCheckInterval,
            DueTime = options.VersionCheckInterval > TimeSpan.FromSeconds(5)
                ? TimeSpan.FromSeconds(2) // start immediately
                : options.VersionCheckInterval,
            Name = "VersionCheck"
        });

        // create start up logger
        LogService.Start(new AppLogSettings
        {
            LogEventNames = AppLogService.GetLogEventNames(options.LogVerbose, UserSettings.DebugData1, UserSettings.Logging.LogEventNames),
            LogAnonymous = options.LogAnonymous ?? Settings.UserSettings.Logging.LogAnonymous,
            LogToConsole = UserSettings.Logging.LogToConsole,
            LogToFile = UserSettings.Logging.LogToFile,
            LogLevel = options.LogVerbose ? LogLevel.Trace : LogLevel.Information
        });

        // add default test public server if not added yet
        ClientProfileService.TryRemoveByTokenId("5aacec55-5cac-457a-acad-3976969236f8"); //remove obsoleted public server
        ClientProfileService.TryRemoveByTokenId("77d58603-cdcb-4efc-992f-c132be1de0e3"); //remove obsoleted public server (bad ad)
        ClientProfileService.TryRemoveByTokenId("9c926156-28fa-4957-9616-38a17e5344ff"); //remove obsoleted public server (bad ad)
        var builtInProfileIds = ClientProfileService.ImportBuiltInAccessKeys(options.AccessKeys);
        Settings.UserSettings.ClientProfileId ??= builtInProfileIds.FirstOrDefault()?.ClientProfileId; // set first one as default

        var uiService = options.UiService ?? new AppUiServiceBase();

        // initialize features
        Features = new AppFeatures
        {
            Version = typeof(VpnHoodApp).Assembly.GetName().Version,
            IsExcludeAppsSupported = Device.IsExcludeAppsSupported,
            IsIncludeAppsSupported = Device.IsIncludeAppsSupported,
            IsAddAccessKeySupported = options.IsAddAccessKeySupported,
            UpdateInfoUrl = options.UpdateInfoUrl,
            UiName = options.UiName,
            BuiltInClientProfileId = builtInProfileIds.FirstOrDefault()?.ClientProfileId,
            IsAccountSupported = options.AccountService != null,
            IsBillingSupported = options.AccountService?.Billing != null,
            IsQuickLaunchSupported = uiService.IsQuickLaunchSupported,
            IsNotificationSupported = uiService.IsNotificationSupported,
            IsAlwaysOnSupported = device.IsAlwaysOnSupported,
            GaMeasurementId = options.AppGa4MeasurementId,
            ClientId = Settings.ClientId.ToString()
        };

        // initialize services
        Services = new AppServices
        {
            AppCultureService = options.CultureService ?? new AppCultureService(this),
            AdServices = options.AdServices,
            AccountService = options.AccountService != null ? new AppAccountService(this, options.AccountService) : null,
            UpdaterService = options.UpdaterService,
            UiService = uiService,
            Tracker = options.Tracker
        };

        // Clear last update status if version has changed
        if (_versionCheckResult != null && _versionCheckResult.LocalVersion != Features.Version)
        {
            _versionCheckResult = null;
            File.Delete(VersionCheckFilePath);
        }

        // Apply settings but no error on start up
        ApplySettings();
        
        // schedule job
        JobRunner.Default.Add(this);
    }

    private void ApplySettings()
    {
        try
        {
            var state = State;
            var client = _client; // it may be null
            var disconnectRequired = false;
            if (client != null)
            {
                client.UseUdpChannel = UserSettings.UseUdpChannel;
                client.DropUdpPackets = UserSettings.DebugData1?.Contains("/drop-udp") == true || UserSettings.DropUdpPackets;

                // check is disconnect required
                disconnectRequired =
                    (_oldUserSettings.TunnelClientCountry != UserSettings.TunnelClientCountry) ||
                    (_activeClientProfileId != null && UserSettings.ClientProfileId != _activeClientProfileId) || //ClientProfileId has been changed
                    (_activeServerLocation != state.ClientServerLocationInfo?.ServerLocation) || //ClientProfileId has been changed
                    (UserSettings.IncludeLocalNetwork != client.IncludeLocalNetwork); // IncludeLocalNetwork has been changed
            }

            // Enable trackers
            if (Services.Tracker != null)
                Services.Tracker.IsEnabled = Settings.UserSettings.AllowAnonymousTracker;

            //lets refresh clientProfile
            _currentClientProfile = null;

            // set ServerLocation to null if the item is SameAsGlobalAuto
            if (UserSettings.ServerLocation != null &&
                CurrentClientProfile?.ServerLocationInfos.FirstOrDefault(x => x.ServerLocation == UserSettings.ServerLocation)?.IsDefault == true)
                UserSettings.ServerLocation = null;

            // sync culture to app settings
            Services.AppCultureService.SelectedCultures =
                UserSettings.CultureCode != null ? [UserSettings.CultureCode] : [];

            InitCulture();
            _oldUserSettings = VhUtil.JsonClone(UserSettings);

            // disconnect
            if (state.CanDisconnect && disconnectRequired)
                _ = Disconnect(true);
        }
        catch (Exception ex)
        {
            ReportError(ex, "Could not apply settings.");
        }
    }

    private ITracker CreateBuildInTracker(string? userAgent)
    {
        if (string.IsNullOrEmpty(_appGa4MeasurementId))
            throw new InvalidOperationException("AppGa4MeasurementId is required to create a built-in tracker.");

        var tracker = new Ga4TagTracker
        {
            MeasurementId = _appGa4MeasurementId,
            SessionCount = 1,
            ClientId = Settings.ClientId.ToString(),
            SessionId = Guid.NewGuid().ToString(),
            UserProperties = new Dictionary<string, object> { { "client_version", Features.Version.ToString(3) } }
        };

        if (!string.IsNullOrEmpty(userAgent))
            tracker.UserAgent = userAgent;

        _ = tracker.Track(new TrackEvent { EventName = TrackEventNames.SessionStart });

        return tracker;
    }

    private void ActiveUiContext_OnChanged(object sender, EventArgs e)
    {
        var uiContext = ActiveUiContext.Context;
        if (IsIdle && _internalAdService?.IsPreloadApEnabled == true && uiContext != null)
            _ = LoadAd(uiContext, CancellationToken.None);
    }

    public ClientProfile? CurrentClientProfile
    {
        get
        {
            if (_currentClientProfile?.ClientProfileId != UserSettings.ClientProfileId)
                _currentClientProfile = ClientProfileService.FindById(UserSettings.ClientProfileId ?? Guid.Empty);
            return _currentClientProfile;
        }
    }

    public AppState State
    {
        get
        {
            var client = _client;
            var connectionState = ConnectionState;
            var appState = new AppState
            {
                ConfigTime = Settings.ConfigTime,
                ConnectionState = connectionState,
                IsIdle = IsIdle,
                CanConnect = connectionState is AppConnectionState.None,
                CanDiagnose = !_hasDiagnoseStarted && (connectionState is AppConnectionState.None or AppConnectionState.Connected or AppConnectionState.Connecting),
                CanDisconnect = !_isDisconnecting && (connectionState
                    is AppConnectionState.Connected or AppConnectionState.Connecting
                    or AppConnectionState.Diagnosing or AppConnectionState.Waiting),
                ClientProfile = CurrentClientProfile?.ToBaseInfo(),
                LogExists = IsIdle && File.Exists(LogService.LogFilePath),
                LastError = _appPersistState.LastError,
                HasDiagnoseStarted = _hasDiagnoseStarted,
                HasDisconnectedByUser = _hasDisconnectedByUser,
                HasProblemDetected = _hasConnectRequested && IsIdle && (_hasDiagnoseStarted || _appPersistState.LastError != null),
                SessionStatus = LastSessionStatus,
                Speed = client?.Stat.Speed ?? new Traffic(),
                AccountTraffic = client?.Stat.AccountTraffic ?? new Traffic(),
                SessionTraffic = client?.Stat.SessionTraffic ?? new Traffic(),
                ClientCountryCode = _appPersistState.ClientCountryCode,
                ClientCountryName = _appPersistState.ClientCountryName,
                IsWaitingForAd = client?.Stat.IsWaitingForAd is true,
                ConnectRequestTime = _connectRequestTime,
                IsUdpChannelSupported = client?.Stat.IsUdpChannelSupported,
                CurrentUiCultureInfo = new UiCultureInfo(CultureInfo.DefaultThreadCurrentUICulture ?? SystemUiCulture),
                SystemUiCultureInfo = new UiCultureInfo(SystemUiCulture),
                VersionStatus = _versionCheckResult?.VersionStatus ?? VersionStatus.Unknown,
                PurchaseState = Services.AccountService?.Billing?.PurchaseState,
                LastPublishInfo = _versionCheckResult?.VersionStatus is VersionStatus.Deprecated or VersionStatus.Old
                    ? _versionCheckResult.PublishInfo
                    : null,
                ServerLocationInfo = client?.Stat.ServerLocationInfo,
                ClientServerLocationInfo = UserSettings.ServerLocation is null
                        ? CurrentClientProfile?.ServerLocationInfos.FirstOrDefault(x => x.IsDefault)
                        : CurrentClientProfile?.ServerLocationInfos.FirstOrDefault(x => x.ServerLocation == UserSettings.ServerLocation)
            };

            return appState;
        }
    }

    public AppConnectionState ConnectionState
    {
        get
        {
            var client = _client;
            if (_isLoadingIpGroup || _isFindingCountryCode) return AppConnectionState.Initializing;
            if (Diagnoser.IsWorking) return AppConnectionState.Diagnosing;
            if (_isDisconnecting || client?.State == ClientState.Disconnecting) return AppConnectionState.Disconnecting;
            if (_isConnecting || client?.State == ClientState.Connecting) return AppConnectionState.Connecting;
            if (client?.State == ClientState.Waiting) return AppConnectionState.Waiting;
            if (client?.Stat.IsWaitingForAd is true) return AppConnectionState.Connecting;
            if (client?.State == ClientState.Connected) return AppConnectionState.Connected;
            return AppConnectionState.None;
        }
    }

    private void FireConnectionStateChanged()
    {
        // check changed state
        var connectionState = ConnectionState;
        if (connectionState == _lastConnectionState) return;
        _lastConnectionState = connectionState;
        try
        {
            ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ReportError(ex, "Could not FireConnectionStateChanged");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Disconnect().VhConfigureAwait();
        Device.Dispose();
        LogService.Dispose();
        DisposeSingleton();
        ActiveUiContext.OnChanged -= ActiveUiContext_OnChanged;
    }

    public static VpnHoodApp Init(IDevice device, AppOptions? options = default)
    {
        return new VpnHoodApp(device, options);
    }

    private void DeviceOnStartedAsService(object sender, EventArgs e)
    {
        var clientProfile = CurrentClientProfile;
        if (clientProfile == null)
        {
            var ex = new Exception("Could not start as service. No server is selected.");
            _appPersistState.LastError = new ApiError(ex);
            throw ex;
        }

        _ = Connect(clientProfile.ClientProfileId);
    }

    public void ClearLastError()
    {
        if (!IsIdle)
            return; //can just set in Idle State

        _appPersistState.LastError = null;
        _hasDiagnoseStarted = false;
        _hasDisconnectedByUser = false;
        _hasDisconnectRequested = false;
        _hasConnectRequested = false;
        _connectRequestTime = null;
    }

    public async Task Connect(Guid? clientProfileId = null, string? serverLocation = null, bool diagnose = false,
        string? userAgent = default, bool throwException = true, CancellationToken cancellationToken = default)
    {
        // disconnect current connection
        if (!IsIdle)
            await Disconnect(true).VhConfigureAwait();

        // initialize built-in tracker after acquire userAgent
        if (Services.Tracker == null && UserSettings.AllowAnonymousTracker && !string.IsNullOrEmpty(_appGa4MeasurementId))
            Services.Tracker = CreateBuildInTracker(userAgent);

        // request features for the first time
        await RequestFeatures(cancellationToken).VhConfigureAwait();

        // set use default clientProfile and serverLocation
        serverLocation ??= UserSettings.ServerLocation;
        clientProfileId ??= UserSettings.ClientProfileId;
        var clientProfile = ClientProfileService.FindById(clientProfileId ?? Guid.Empty) ?? throw new NotExistsException("Could not find any VPN profile to connect.");

        // set current profile only if it has been updated to avoid unnecessary new config time
        if (clientProfile.ClientProfileId != UserSettings.ClientProfileId || serverLocation != UserSettings.ServerLocation)
        {
            UserSettings.ClientProfileId = clientProfile.ClientProfileId;
            UserSettings.ServerLocation = serverLocation;
            Settings.Save();
        }

        try
        {
            // prepare logger
            ClearLastError();
            _activeClientProfileId = clientProfileId;
            _activeServerLocation = State.ClientServerLocationInfo?.ServerLocation;
            _isConnecting = true;
            _hasDisconnectedByUser = false;
            _hasConnectRequested = true;
            _hasDisconnectRequested = false;
            _hasDiagnoseStarted = diagnose;
            _connectRequestTime = DateTime.Now;
            FireConnectionStateChanged();
            LogService.Start(new AppLogSettings
            {
                LogEventNames = AppLogService.GetLogEventNames(_logVerbose, UserSettings.DebugData1, UserSettings.Logging.LogEventNames),
                LogAnonymous = _logAnonymous ?? Settings.UserSettings.Logging.LogAnonymous,
                LogToConsole = UserSettings.Logging.LogToConsole,
                LogToFile = UserSettings.Logging.LogToFile | diagnose,
                LogLevel = _logVerbose || diagnose ? LogLevel.Trace : LogLevel.Information
            });

            // log general info
            VhLogger.Instance.LogInformation("AppVersion: {AppVersion}", GetType().Assembly.GetName().Version);
            VhLogger.Instance.LogInformation("Time: {Time}", DateTime.UtcNow.ToString("u", new CultureInfo("en-US")));
            VhLogger.Instance.LogInformation("OS: {OsInfo}", Device.OsInfo);
            VhLogger.Instance.LogInformation("UserAgent: {userAgent}", userAgent);
            VhLogger.Instance.LogInformation("UserSettings: {UserSettings}",
                JsonSerializer.Serialize(UserSettings, new JsonSerializerOptions { WriteIndented = true }));

            // log country name
            if (diagnose)
                VhLogger.Instance.LogInformation("Country: {Country}", GetCountryName(await GetClientCountryCode(cancellationToken).VhConfigureAwait()));

            VhLogger.Instance.LogInformation("VpnHood Client is Connecting ...");

            // create cancellationToken
            _connectCts = new CancellationTokenSource();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _connectCts.Token);
            cancellationToken = linkedCts.Token;

            // connect
            await ConnectInternal(clientProfile.Token, _activeServerLocation, userAgent, true, cancellationToken).VhConfigureAwait();
        }
        catch (Exception ex)
        {
            ReportError(ex, "Could not connect.");

            //user may disconnect before connection closed
            if (!_hasDisconnectedByUser)
                _appPersistState.LastError = ex is OperationCanceledException
                    ? new ApiError(new Exception("Could not connect to any server.", ex))
                    : new ApiError(ex);

            // don't wait for disconnect, it may cause deadlock
            _ = Disconnect();

            if (throwException)
            {
                if (_hasDisconnectedByUser)
                    throw new OperationCanceledException("Connection has been canceled by the user.", ex);

                throw;
            }
        }
        finally
        {
            _connectCts = null;
            _isConnecting = false;
            FireConnectionStateChanged();
        }
    }

    private async Task<IPacketCapture> CreatePacketCapture()
    {
        if (UserSettings.DebugData1?.Contains("/null-capture") is true)
        {
            VhLogger.Instance.LogWarning("Using NullPacketCapture. No packet will go through the VPN.");
            return new NullPacketCapture();
        }

        // create packet capture
        var packetCapture = await Device.CreatePacketCapture(ActiveUiContext.Context).VhConfigureAwait();

        // init packet capture
        if (packetCapture.IsMtuSupported)
            packetCapture.Mtu = TunnelDefaults.MtuWithoutFragmentation;

        // App filters
        if (packetCapture.CanExcludeApps && UserSettings.AppFiltersMode == FilterMode.Exclude)
            packetCapture.ExcludeApps = UserSettings.AppFilters;

        if (packetCapture.CanIncludeApps && UserSettings.AppFiltersMode == FilterMode.Include)
            packetCapture.IncludeApps = UserSettings.AppFilters;

        return packetCapture;
    }

    private async Task ConnectInternal(Token token, string? serverLocationInfo, string? userAgent,
        bool allowUpdateToken, CancellationToken cancellationToken)
    {
        // show token info
        VhLogger.Instance.LogInformation("TokenId: {TokenId}, SupportId: {SupportId}",
            VhLogger.FormatId(token.TokenId), VhLogger.FormatId(token.SupportId));

        // calculate packetCaptureIpRanges
        var packetCaptureIpRanges = new IpRangeOrderedList(IpNetwork.All.ToIpRanges());
        if (UserSettings.PacketCaptureIncludeIpRanges.Any())
            packetCaptureIpRanges = packetCaptureIpRanges.Intersect(UserSettings.PacketCaptureIncludeIpRanges);

        if (UserSettings.PacketCaptureExcludeIpRanges.Any())
            packetCaptureIpRanges = packetCaptureIpRanges.Exclude(UserSettings.PacketCaptureExcludeIpRanges);

        // create clientOptions
        var clientOptions = new ClientOptions
        {
            SessionTimeout = SessionTimeout,
            ReconnectTimeout = _reconnectTimeout,
            AutoWaitTimeout = _autoWaitTimeout,
            IncludeLocalNetwork = UserSettings.IncludeLocalNetwork,
            IpRangeProvider = this,
            AdProvider = this,
            PacketCaptureIncludeIpRanges = packetCaptureIpRanges,
            MaxDatagramChannelCount = UserSettings.MaxDatagramChannelCount,
            ConnectTimeout = TcpTimeout,
            ServerQueryTimeout = _serverQueryTimeout,
            DropUdpPackets = UserSettings.DebugData1?.Contains("/drop-udp") == true || UserSettings.DropUdpPackets,
            ServerLocation = serverLocationInfo == ServerLocationInfo.Auto.ServerLocation ? null : serverLocationInfo,
            UseUdpChannel = UserSettings.UseUdpChannel,
            DomainFilter = UserSettings.DomainFilter,
            ForceLogSni = LogService.LogEvents.Contains(nameof(GeneralEventId.Sni), StringComparer.OrdinalIgnoreCase),
            AllowAnonymousTracker = UserSettings.AllowAnonymousTracker,
            AllowEndPointTracker = UserSettings.AllowAnonymousTracker && _allowEndPointTracker,
            Tracker = Services.Tracker
        };

        if (_socketFactory != null) clientOptions.SocketFactory = _socketFactory;
        if (userAgent != null) clientOptions.UserAgent = userAgent;


        // Create Client with a new PacketCapture
        if (_client != null) throw new Exception("Last client has not been disposed properly.");
        var packetCapture = await CreatePacketCapture().VhConfigureAwait();
        
        
        VpnHoodClient? client = null;

        try
        {
            client = new VpnHoodClient(packetCapture, Settings.ClientId, token, clientOptions);
            client.StateChanged += Client_StateChanged;
            _client = client;

            if (_hasDiagnoseStarted)
                await Diagnoser.Diagnose(client, cancellationToken).VhConfigureAwait();
            else if (_autoDiagnose)
                await Diagnoser.Connect(client, cancellationToken).VhConfigureAwait();
            else
                await client.Connect(cancellationToken).VhConfigureAwait();

            // set connected time
            ConnectedTime = DateTime.Now;

            // update access token if ResponseAccessKey is set
            if (client.ResponseAccessKey != null)
                token = ClientProfileService.UpdateTokenByAccessKey(token, client.ResponseAccessKey);

            // check version after first connection
            _ = VersionCheck();
        }
        catch (Exception) when (client is not null)
        {
            await client.DisposeAsync().VhConfigureAwait();
            _client = null;

            // try to update token from url after connection or error if ResponseAccessKey is not set
            // check _client is not null to make sure 
            if (allowUpdateToken && !string.IsNullOrEmpty(token.ServerToken.Url) &&
                await ClientProfileService.UpdateServerTokenByUrl(token).VhConfigureAwait())
            {
                token = ClientProfileService.GetToken(token.TokenId);
                await ConnectInternal(token, serverLocationInfo, userAgent, false, cancellationToken).VhConfigureAwait();
                return;
            }

            throw;
        }
        catch (Exception)
        {
            packetCapture.Dispose(); // don't miss to dispose when there is no client to handle it
            throw;
        }
    }

    private async Task RequestFeatures(CancellationToken cancellationToken)
    {
        // QuickLaunch
        if (ActiveUiContext.Context != null &&
            Services.UiService.IsQuickLaunchSupported &&
            Settings.IsQuickLaunchEnabled is null)
        {
            try
            {
                Settings.IsQuickLaunchEnabled =
                    await Services.UiService.RequestQuickLaunch(ActiveUiContext.RequiredContext, cancellationToken).VhConfigureAwait();
            }
            catch (Exception ex)
            {
                ReportError(ex, "Could not add QuickLaunch.");
            }

            Settings.Save();
        }

        // Notification
        if (ActiveUiContext.Context != null &&
            Services.UiService.IsNotificationSupported &&
            Settings.IsNotificationEnabled is null)
        {
            try
            {
                Settings.IsNotificationEnabled =
                    await Services.UiService.RequestNotification(ActiveUiContext.RequiredContext, cancellationToken).VhConfigureAwait();
            }
            catch (Exception ex)
            {
                ReportError(ex, "Could not enable Notification.");
            }

            Settings.Save();
        }
    }

    public CultureInfo SystemUiCulture => new(
        Services.AppCultureService.SystemCultures.FirstOrDefault()?.Split("-").FirstOrDefault()
        ?? CultureInfo.InstalledUICulture.TwoLetterISOLanguageName);

    private void InitCulture()
    {
        // set default culture
        var firstSelected = Services.AppCultureService.SelectedCultures.FirstOrDefault();
        CultureInfo.CurrentUICulture = (firstSelected != null) ? new CultureInfo(firstSelected) : SystemUiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;

        // sync UserSettings from the System App Settings
        UserSettings.CultureCode = firstSelected?.Split("-").FirstOrDefault();
    }

    private void SettingsBeforeSave(object sender, EventArgs e)
    {
        ApplySettings();
    }

    public static string GetCountryName(string countryCode)
    {
        try { return new RegionInfo(countryCode).Name; }
        catch { return countryCode; }
    }

    public async Task<string> GetClientCountryCode(CancellationToken cancellationToken)
    {
        _isFindingCountryCode = true;

        if (_appPersistState.ClientCountryCode == null && _useExternalLocationService)
        {
            try
            {
                _appPersistState.ClientCountryCode = await IPAddressUtil.GetCountryCodeByCloudflare(cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                ReportError(ex, "Could not get country code from Cloudflare service.");
            }
        }


        if (_appPersistState.ClientCountryCode == null && _useExternalLocationService)
        {
            try
            {
                using var cancellationTokenSource = new CancellationTokenSource(5000);
                var ipLocationProvider = new IpLocationProviderFactory().CreateDefault("VpnHood-Client");
                var ipLocation = await ipLocationProvider.GetLocation(new HttpClient(), cancellationTokenSource.Token).VhConfigureAwait();
                _appPersistState.ClientCountryCode = ipLocation.CountryCode;
            }
            catch (Exception ex)
            {
                ReportError(ex, "Could not get country code from IpApi service.");
            }
        }

        // try to get by ip group (GetCountryCodeByCurrentIp use external service)
        if (_appPersistState.ClientCountryCode == null && _useInternalLocationService && _useExternalLocationService)
        {
            try
            {
                var ipGroupManager = await GetIpGroupManager().VhConfigureAwait();
                _appPersistState.ClientCountryCode ??= await ipGroupManager.GetCountryCodeByCurrentIp().VhConfigureAwait();
            }
            catch (Exception ex)
            {
                ReportError(ex, "Could not find country code.");
            }
        }

        // return last country
        _isFindingCountryCode = false;
        return _appPersistState.ClientCountryCode ?? RegionInfo.CurrentRegion.Name;
    }

    public async Task LoadAd(IUiContext uiContext, CancellationToken cancellationToken)
    {
        if (_internalAdService == null)
            throw new Exception("AdService has not been initialized.");

        var countryCode = await GetClientCountryCode(cancellationToken);
        await _internalAdService.LoadAd(uiContext, countryCode: countryCode, forceReload: false, cancellationToken);
    }

    public async Task<string> ShowAd(string sessionId, CancellationToken cancellationToken)
    {
        if (_internalAdService == null)
            throw new Exception("AdService has not been initialized.");

        var adData = $"sid:{sessionId};ad:{Guid.NewGuid()}";
        try
        {
            await LoadAd(ActiveUiContext.RequiredContext, cancellationToken);
            await _internalAdService.ShowAd(ActiveUiContext.RequiredContext, adData, cancellationToken);
            return adData;
        }
        catch (UiContextNotAvailableException)
        {
            throw new ShowAdNoUiException();
        }
    }

    private void Client_StateChanged(object sender, EventArgs e)
    {
        // do not disconnect in _isConnecting by ClientState, because the AutoUploadToken from url & reconnect may inprogress
        // _isConnecting will disconnect the connection by try catch
        if (_client?.State == ClientState.Disposed && !_isConnecting)
        {
            _ = Disconnect();
            return;
        }

        FireConnectionStateChanged();
    }


    private readonly AsyncLock _disconnectLock = new();

    public async Task Disconnect(bool byUser = false)
    {
        using var lockAsync = await _disconnectLock.LockAsync().VhConfigureAwait();
        if (_isDisconnecting || _hasDisconnectRequested)
            return;
        _hasDisconnectRequested = true;

        try
        {
            // set disconnect reason by user
            _hasDisconnectedByUser = byUser;
            if (byUser)
                VhLogger.Instance.LogInformation("User has requested disconnection.");

            // change state to disconnecting
            _isDisconnecting = true;
            FireConnectionStateChanged();

            // check diagnose
            if (_hasDiagnoseStarted && _appPersistState.LastError == null)
                _appPersistState.LastError = new ApiError(new Exception("Diagnoser has finished and no issue has been detected."));

            // cancel current connecting if any
            _connectCts?.Cancel();

            // close client
            // do not wait for bye if user request disconnection
            if (_client != null)
                await _client.DisposeAsync(waitForBye: !byUser).VhConfigureAwait();

            LogService.Stop();
        }
        catch (Exception ex)
        {
            ReportError(ex, "Error in disconnecting.");
        }
        finally
        {
            _appPersistState.LastError ??= LastSessionStatus?.Error;
            _activeClientProfileId = null;
            _activeServerLocation = null;
            _lastSessionStatus = _client?.SessionStatus;
            _isConnecting = false;
            _isDisconnecting = false;
            ConnectedTime = null;
            _client = null;
            FireConnectionStateChanged();
        }
    }

    public async Task<IpGroupManager> GetIpGroupManager()
    {
        if (_ipGroupManager != null)
            return _ipGroupManager;

        var zipArchive = new ZipArchive(new MemoryStream(App.Resource.IpLocations), ZipArchiveMode.Read, leaveOpen: false);
        _ipGroupManager = await IpGroupManager.Create(zipArchive).VhConfigureAwait();
        return _ipGroupManager;
    }

    public void VersionCheckPostpone()
    {
        // version status is unknown when app container can do it
        if (Services.UpdaterService != null)
        {
            _versionCheckResult = null;
            File.Delete(VersionCheckFilePath);
        }

        // set latest ignore time
        _appPersistState.UpdateIgnoreTime = DateTime.Now;
    }

    private readonly AsyncLock _versionCheckLock = new();
    public async Task VersionCheck(bool force = false)
    {
        using var lockAsync = await _versionCheckLock.LockAsync().VhConfigureAwait();
        if (!force && _appPersistState.UpdateIgnoreTime + _versionCheckInterval > DateTime.Now)
            return;

        // check version by app container
        try
        {
            if (ActiveUiContext.Context != null && Services.UpdaterService != null &&
                await Services.UpdaterService.Update(ActiveUiContext.RequiredContext).VhConfigureAwait())
            {
                VersionCheckPostpone();
                return;
            }
        }
        catch (Exception ex)
        {
            ReportWarning(ex, "Could not check version by VersionCheck.");
        }

        // check version by UpdateInfoUrl
        _versionCheckResult = await VersionCheckByUpdateInfo().VhConfigureAwait();

        // save the result
        if (_versionCheckResult != null)
            await File.WriteAllTextAsync(VersionCheckFilePath, JsonSerializer.Serialize(_versionCheckResult)).VhConfigureAwait();

        else if (File.Exists(VersionCheckFilePath))
            File.Delete(VersionCheckFilePath);
    }

    private async Task<VersionCheckResult?> VersionCheckByUpdateInfo()
    {
        try
        {
            if (Features.UpdateInfoUrl == null)
                return null; // no update info url. Job done

            VhLogger.Instance.LogTrace("Retrieving the latest publish info...");

            using var httpClient = new HttpClient();
            var publishInfoJson = await httpClient.GetStringAsync(Features.UpdateInfoUrl).VhConfigureAwait();
            var latestPublishInfo = VhUtil.JsonDeserialize<PublishInfo>(publishInfoJson);
            VersionStatus versionStatus;

            // Check version
            if (latestPublishInfo.Version == null)
                throw new Exception("Version is not available in publish info.");

            // set default notification delay
            if (Features.Version <= latestPublishInfo.DeprecatedVersion)
                versionStatus = VersionStatus.Deprecated;

            else if (Features.Version < latestPublishInfo.Version &&
                     DateTime.UtcNow - latestPublishInfo.ReleaseDate > latestPublishInfo.NotificationDelay)
                versionStatus = VersionStatus.Old;

            else
                versionStatus = VersionStatus.Latest;

            // save the result
            var checkResult = new VersionCheckResult
            {
                LocalVersion = Features.Version,
                VersionStatus = versionStatus,
                PublishInfo = latestPublishInfo,
                CheckedTime = DateTime.UtcNow
            };

            VhLogger.Instance.LogInformation(
                "The latest publish info has been retrieved. VersionStatus: {VersionStatus}, LatestVersion: {LatestVersion}",
                versionStatus, latestPublishInfo.Version);

            return checkResult; // Job done
        }
        catch (Exception ex)
        {
            ReportWarning(ex, "Could not retrieve the latest publish info information.");
            return null; // could not retrieve the latest publish info. try later
        }
    }

    public async Task<IpRangeOrderedList?> GetIncludeIpRanges(IPAddress clientIp, CancellationToken cancellationToken)
    {
        // calculate packetCaptureIpRanges
        var ipRanges = IpNetwork.All.ToIpRanges();
        if (UserSettings.IncludeIpRanges.Any()) ipRanges = ipRanges.Intersect(UserSettings.IncludeIpRanges);
        if (UserSettings.ExcludeIpRanges.Any()) ipRanges = ipRanges.Exclude(UserSettings.ExcludeIpRanges);

        // exclude client country IPs
        if (UserSettings.TunnelClientCountry)
            return ipRanges;

        try
        {
            var lastCountryCode = await GetClientCountryCode(cancellationToken).VhConfigureAwait();
            VhLogger.Instance.LogTrace("Finding Country IPs for split tunneling. LastCountry: {Country}", GetCountryName(lastCountryCode));
            _isLoadingIpGroup = true;
            FireConnectionStateChanged();

            if (!_useInternalLocationService)
                throw new InvalidOperationException("Could not use internal location service because it is disabled.");

            var ipGroupManager = await GetIpGroupManager().VhConfigureAwait();
            var ipGroup = await ipGroupManager.GetIpGroup(clientIp, lastCountryCode).VhConfigureAwait();
            _appPersistState.ClientCountryCode = ipGroup.IpGroupId;
            VhLogger.Instance.LogInformation("Client Country is: {Country}", _appPersistState.ClientCountryName);
            ipRanges = ipRanges.Exclude(ipGroup.IpRanges);

        }
        catch (Exception ex)
        {
            ReportError(ex, "Could not get ip locations of your country.");
            if (!UserSettings.TunnelClientCountry)
            {
                UserSettings.TunnelClientCountry = true;
                Settings.Save();
            }
        }

        finally
        {
            _isLoadingIpGroup = false;
        }

        return ipRanges;
    }

    public async Task RefreshAccount(bool updateCurrentClientProfile = false)
    {
        if (Services.AccountService is not AppAccountService accountService)
            throw new Exception("AccountService is not initialized.");

        // clear cache
        accountService.ClearCache();

        // update profiles
        // get access tokens from account
        var account = await Services.AccountService.GetAccount().VhConfigureAwait();
        var accessKeys = account?.SubscriptionId != null
            ? await Services.AccountService.GetAccessKeys(account.SubscriptionId).VhConfigureAwait()
            : [];
        ClientProfileService.UpdateFromAccount(accessKeys);

        // Select the best client profile from their account.
        if (updateCurrentClientProfile)
        {
            var clientProfiles = ClientProfileService
                .List()
                .Where(x => x.IsForAccount)
                .ToArray();

            if (clientProfiles.Any())
            {
                UserSettings.ClientProfileId = clientProfiles.Last().ClientProfileId;
                Settings.Save();
            }
        }

        // update current profile if removed
        if (ClientProfileService.FindById(UserSettings.ClientProfileId ?? Guid.Empty) == null)
        {
            var clientProfiles = ClientProfileService.List();
            UserSettings.ClientProfileId = clientProfiles.Length == 1 ? clientProfiles.First().ClientProfileId : null;
            Settings.Save();
        }
    }

    private void ReportError(Exception ex, string message, [CallerMemberName] string action = "n/a")
    {
        Services.Tracker?.VhTrackErrorAsync(ex, message, action);
        VhLogger.Instance.LogError(ex, message);
    }

    private void ReportWarning(Exception ex, string message, [CallerMemberName] string action = "n/a")
    {
        Services.Tracker?.VhTrackWarningAsync(ex, message, action);
        VhLogger.Instance.LogWarning(ex, message);
    }

    public Task RunJob()
    {
        return VersionCheck();
    }

    public void UpdateUi()
    {
        ApplySettings();
        UiHasChanged?.Invoke(this, EventArgs.Empty);
    }
}
