using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Client.Abstractions;
using VpnHood.Client.App.ClientProfiles;
using VpnHood.Client.App.Services;
using VpnHood.Client.App.Settings;
using VpnHood.Client.Device;
using VpnHood.Client.Diagnosing;
using VpnHood.Common;
using VpnHood.Common.Exceptions;
using VpnHood.Common.IpLocations;
using VpnHood.Common.Jobs;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
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
    private readonly bool _useIpGroupManager;
    private readonly bool _useExternalLocationService;
    private readonly string? _appGa4MeasurementId;
    private bool _hasConnectRequested;
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
    private readonly TimeSpan _versionCheckInterval;
    private readonly AppPersistState _appPersistState;
    private readonly TimeSpan _reconnectTimeout;
    private readonly TimeSpan _autoWaitTimeout;
    private CancellationTokenSource? _connectCts;
    private ClientProfile? _currentClientProfile;
    private VersionCheckResult? _versionCheckResult;
    private VpnHoodClient? _client;
    private readonly bool? _logVerbose;
    private readonly bool? _logAnonymous;
    private SessionStatus? LastSessionStatus => _client?.SessionStatus ?? _lastSessionStatus;
    private string TempFolderPath => Path.Combine(StorageFolderPath, "Temp");
    private string IpGroupsFolderPath => Path.Combine(TempFolderPath, "ipgroups");
    private string VersionCheckFilePath => Path.Combine(StorageFolderPath, "version.json");

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
    public TimeSpan TcpTimeout { get; set; } = new ClientOptions().ConnectTimeout;
    public AppLogService LogService { get; }
    public AppResource Resource { get; }
    public AppServices Services { get; }
    public DateTime? ConnectedTime { get; private set; }
    public IUiContext? UiContext { get; set; }
    public IUiContext RequiredUiContext => UiContext ?? throw new Exception("The main window app does not exists.");

    private VpnHoodApp(IDevice device, AppOptions? options = default)
    {
        options ??= new AppOptions();
        Directory.CreateDirectory(options.StorageFolderPath); //make sure directory exists
        Resource = options.Resource;

        Device = device;
        device.StartedAsService += DeviceOnStartedAsService;

        StorageFolderPath = options.StorageFolderPath ??
                            throw new ArgumentNullException(nameof(options.StorageFolderPath));
        Settings = AppSettings.Load(Path.Combine(StorageFolderPath, FileNameSettings));
        Settings.BeforeSave += SettingsBeforeSave;
        ClientProfileService = new ClientProfileService(Path.Combine(StorageFolderPath, FolderNameProfiles));
        SessionTimeout = options.SessionTimeout;
        _socketFactory = options.SocketFactory;
        _useIpGroupManager = options.UseIpGroupManager;
        _useExternalLocationService = options.UseExternalLocationService;
        _appGa4MeasurementId = options.AppGa4MeasurementId;
        _versionCheckInterval = options.VersionCheckInterval;
        _reconnectTimeout = options.ReconnectTimeout;
        _autoWaitTimeout = options.AutoWaitTimeout;
        _appPersistState = AppPersistState.Load(Path.Combine(StorageFolderPath, FileNamePersistState));
        _versionCheckResult = VhUtil.JsonDeserializeFile<VersionCheckResult>(VersionCheckFilePath);
        _logVerbose = options.LogVerbose;
        _logAnonymous = options.LogAnonymous;
        Diagnoser.StateChanged += (_, _) => FireConnectionStateChanged();
        LogService = new AppLogService(Path.Combine(StorageFolderPath, FileNameLog));

        // configure update job section
        JobSection = new JobSection(new JobOptions
        {
            Interval = options.VersionCheckInterval,
            DueTime = options.VersionCheckInterval > TimeSpan.FromSeconds(5)
                ? TimeSpan.FromSeconds(3)
                : options.VersionCheckInterval,
            Name = "VersionCheck"
        });

        // create start up logger
        if (!device.IsLogToConsoleSupported) UserSettings.Logging.LogToConsole = false;
        LogService.Start(new AppLogSettings
        {
            LogVerbose = options.LogVerbose ?? Settings.UserSettings.Logging.LogVerbose,
            LogAnonymous = options.LogAnonymous ?? Settings.UserSettings.Logging.LogAnonymous,
            LogToConsole = UserSettings.Logging.LogToConsole,
            LogToFile = UserSettings.Logging.LogToFile
        });

        // add default test public server if not added yet
        ClientProfileService.TryRemoveByTokenId("5aacec55-5cac-457a-acad-3976969236f8"); //remove obsoleted public server
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
            IsAlwaysOnSupported = device.IsAlwaysOnSupported
        };

        // initialize services
        Services = new AppServices
        {
            AppCultureService = options.CultureService ?? new AppCultureService(this),
            AdService = options.AdService,
            AccountService =
                options.AccountService != null ? new AppAccountService(this, options.AccountService) : null,
            UpdaterService = options.UpdaterService,
            UiService = uiService
        };

        // Clear last update status if version has changed
        if (_versionCheckResult != null && _versionCheckResult.LocalVersion != Features.Version)
        {
            _versionCheckResult = null;
            File.Delete(VersionCheckFilePath);
        }

        // initialize
        InitCulture();
        JobRunner.Default.Add(this);
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
                LastError = _appPersistState.LastErrorMessage,
                HasDiagnoseStarted = _hasDiagnoseStarted,
                HasDisconnectedByUser = _hasDisconnectedByUser,
                HasProblemDetected = _hasConnectRequested && IsIdle && (_hasDiagnoseStarted || _appPersistState.LastErrorMessage != null),
                SessionStatus = LastSessionStatus,
                Speed = _client?.Stat.Speed ?? new Traffic(),
                AccountTraffic = _client?.Stat.AccountTraffic ?? new Traffic(),
                SessionTraffic = _client?.Stat.SessionTraffic ?? new Traffic(),
                ClientCountryCode = _appPersistState.ClientCountryCode,
                ClientCountryName = _appPersistState.ClientCountryName,
                IsWaitingForAd = _client?.Stat.IsWaitingForAd is true,
                ConnectRequestTime = _connectRequestTime,
                IsUdpChannelSupported = _client?.Stat.IsUdpChannelSupported,
                CurrentUiCultureInfo = new UiCultureInfo(CultureInfo.DefaultThreadCurrentUICulture),
                SystemUiCultureInfo = new UiCultureInfo(SystemUiCulture),
                VersionStatus = _versionCheckResult?.VersionStatus ?? VersionStatus.Unknown,
                PurchaseState = Services.AccountService?.Billing?.PurchaseState,
                LastPublishInfo = _versionCheckResult?.VersionStatus is VersionStatus.Deprecated or VersionStatus.Old
                    ? _versionCheckResult.PublishInfo
                    : null,
                ServerLocationInfo = _client?.Stat.ServerLocationInfo,
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
            if (_isLoadingIpGroup) return AppConnectionState.Initializing;
            if (Diagnoser.IsWorking) return AppConnectionState.Diagnosing;
            if (_isDisconnecting || _client?.State == ClientState.Disconnecting) return AppConnectionState.Disconnecting;
            if (_isConnecting || _client?.State == ClientState.Connecting) return AppConnectionState.Connecting;
            if (_client?.State == ClientState.Waiting) return AppConnectionState.Waiting;
            if (_client?.Stat.IsWaitingForAd is true) return AppConnectionState.Connecting;
            if (_client?.State == ClientState.Connected) return AppConnectionState.Connected;
            return AppConnectionState.None;
        }
    }

    private void FireConnectionStateChanged()
    {
        // check changed state
        var connectionState = ConnectionState;
        if (connectionState == _lastConnectionState) return;
        _lastConnectionState = connectionState;
        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await Disconnect().VhConfigureAwait();
        Device.Dispose();
        LogService.Dispose();
        DisposeSingleton();
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
            _appPersistState.LastErrorMessage = "Could not start as service. No server is selected.";
            throw new Exception(_appPersistState.LastErrorMessage);
        }

        _ = Connect(clientProfile.ClientProfileId);
    }

    public void ClearLastError()
    {
        if (!IsIdle)
            return; //can just set in Idle State

        _appPersistState.LastErrorMessage = null;
        _hasDiagnoseStarted = false;
        _hasDisconnectedByUser = false;
        _hasConnectRequested = false;
        _connectRequestTime = null;
    }

    public async Task Connect(Guid? clientProfileId = null, string? serverLocation = null, bool diagnose = false,
        string? userAgent = default, bool throwException = true, CancellationToken cancellationToken = default)
    {
        // disconnect current connection
        if (!IsIdle)
            await Disconnect(true).VhConfigureAwait();

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
            _hasDiagnoseStarted = diagnose;
            _connectRequestTime = DateTime.Now;
            FireConnectionStateChanged();
            LogService.Start(new AppLogSettings
            {
                LogVerbose = _logVerbose ?? Settings.UserSettings.Logging.LogVerbose | diagnose,
                LogAnonymous = _logAnonymous ?? Settings.UserSettings.Logging.LogAnonymous,
                LogToConsole = UserSettings.Logging.LogToConsole,
                LogToFile = UserSettings.Logging.LogToFile | diagnose
            });


            // log general info
            VhLogger.Instance.LogInformation("AppVersion: {AppVersion}", GetType().Assembly.GetName().Version);
            VhLogger.Instance.LogInformation("Time: {Time}", DateTime.UtcNow.ToString("u", new CultureInfo("en-US")));
            VhLogger.Instance.LogInformation("OS: {OsInfo}", Device.OsInfo);
            VhLogger.Instance.LogInformation("UserAgent: {userAgent}", userAgent);
            VhLogger.Instance.LogInformation("UserSettings: {UserSettings}",
                JsonSerializer.Serialize(UserSettings, new JsonSerializerOptions { WriteIndented = true }));

            // it slows down tests and does not need to be logged in normal situation
            if (diagnose)
                VhLogger.Instance.LogInformation("Country: {Country}", await GetClientCountry().VhConfigureAwait());

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
            //user may disconnect before connection closed
            if (!_hasDisconnectedByUser)
            {
                VhLogger.Instance.LogError(ex.Message);
                _appPersistState.LastErrorMessage = ex.Message;
            }

            // don't wait for disconnect, it may cause deadlock
            _ = Disconnect();

            if (throwException)
                throw;
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
        // create packet capture
        var packetCapture = await Device.CreatePacketCapture(UiContext).VhConfigureAwait();

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
        VhLogger.Instance.LogInformation(
            $"TokenId: {VhLogger.FormatId(token.TokenId)}, SupportId: {VhLogger.FormatId(token.SupportId)}");

        // calculate packetCaptureIpRanges
        var packetCaptureIpRanges = IpNetwork.All.ToIpRanges();
        if (!VhUtil.IsNullOrEmpty(UserSettings.PacketCaptureIncludeIpRanges))
            packetCaptureIpRanges = packetCaptureIpRanges.Intersect(UserSettings.PacketCaptureIncludeIpRanges);
        if (!VhUtil.IsNullOrEmpty(UserSettings.PacketCaptureExcludeIpRanges))
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
            PacketCaptureIncludeIpRanges = packetCaptureIpRanges.ToArray(),
            MaxDatagramChannelCount = UserSettings.MaxDatagramChannelCount,
            ConnectTimeout = TcpTimeout,
            AllowAnonymousTracker = UserSettings.AllowAnonymousTracker,
            DropUdpPackets = UserSettings.DebugData1?.Contains("/drop-udp") == true || UserSettings.DropUdpPackets,
            AppGa4MeasurementId = _appGa4MeasurementId,
            ServerLocation = serverLocationInfo == ServerLocationInfo.Auto.ServerLocation ? null : serverLocationInfo,
            UseUdpChannel = UserSettings.UseUdpChannel
        };

        if (_socketFactory != null) clientOptions.SocketFactory = _socketFactory;
        if (userAgent != null) clientOptions.UserAgent = userAgent;

        // Create Client with a new PacketCapture
        if (_client != null) throw new Exception("Last client has not been disposed properly.");
        var packetCapture = await CreatePacketCapture().VhConfigureAwait();
        _client = new VpnHoodClient(packetCapture, Settings.ClientId, token, clientOptions);
        _client.StateChanged += Client_StateChanged;

        try
        {
            if (_hasDiagnoseStarted)
                await Diagnoser.Diagnose(_client, cancellationToken).VhConfigureAwait();
            else
                await Diagnoser.Connect(_client, cancellationToken).VhConfigureAwait();

            // set connected time
            ConnectedTime = DateTime.Now;

            // update access token if ResponseAccessKey is set
            if (_client.ResponseAccessKey != null)
                token = ClientProfileService.UpdateTokenByAccessKey(token, _client.ResponseAccessKey);

            // check version after first connection
            _ = VersionCheck();
        }
        catch (Exception) when (_client is null)
        {
            packetCapture.Dispose(); // don't miss to dispose when there is no client to handle it
        }
        catch (Exception)
        {
            await _client.DisposeAsync().VhConfigureAwait();
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
    }

    private async Task RequestFeatures(CancellationToken cancellationToken)
    {
        // QuickLaunch
        if (UiContext != null &&
            Services.UiService.IsQuickLaunchSupported &&
            Settings.IsQuickLaunchEnabled is null)
        {
            try
            {
                Settings.IsQuickLaunchEnabled =
                    await Services.UiService.RequestQuickLaunch(RequiredUiContext, cancellationToken).VhConfigureAwait();
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, "Could not add QuickLaunch.");
            }

            Settings.Save();
        }

        // Notification
        if (UiContext != null &&
            Services.UiService.IsNotificationSupported &&
            Settings.IsNotificationEnabled is null)
        {
            try
            {
                Settings.IsNotificationEnabled =
                    await Services.UiService.RequestNotification(RequiredUiContext, cancellationToken).VhConfigureAwait();
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, "Could not enable Notification.");
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
        var state = State;
        if (_client != null)
        {
            var client = _client; // it may get null
            client.UseUdpChannel = UserSettings.UseUdpChannel;
            client.DropUdpPackets = UserSettings.DebugData1?.Contains("/drop-udp") == true || UserSettings.DropUdpPackets;

            // check is disconnect required
            var disconnectRequired =
                (_activeClientProfileId != null && UserSettings.ClientProfileId != _activeClientProfileId) || //ClientProfileId has been changed
                (state.CanDisconnect && _activeServerLocation != state.ClientServerLocationInfo?.ServerLocation) || //ClientProfileId has been changed
                (state.CanDisconnect && UserSettings.IncludeLocalNetwork != client.IncludeLocalNetwork); // IncludeLocalNetwork has been changed

            // disconnect
            if (state.CanDisconnect && disconnectRequired)
                _ = Disconnect(true);
        }

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
    }

    public async Task<string?> GetClientCountry()
    {
        // try to get by external service
        if (_appPersistState.ClientCountryCode == null && _useExternalLocationService)
        {
            try
            {
                var ipLocationProvider = new IpLocationProviderFactory().CreateDefault("VpnHood-Client");
                var ipLocation = await ipLocationProvider.GetLocation(new HttpClient()).VhConfigureAwait();
                _appPersistState.ClientCountryCode = ipLocation.CountryCode;
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, "Could not get country code from IpApi service.");
            }
        }

        // try to get by ip group
        if (_appPersistState.ClientCountryCode == null && _useIpGroupManager)
        {
            try
            {
                var ipGroupManager = await GetIpGroupManager().VhConfigureAwait();
                _appPersistState.ClientCountryCode ??= await ipGroupManager.GetCountryCodeByCurrentIp().VhConfigureAwait();
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, "Could not find country code.");
            }
        }

        // return last country
        return _appPersistState.ClientCountryName;
    }

    public async Task<string> ShowAd(string sessionId, CancellationToken cancellationToken)
    {
        if (Services.AdService == null) throw new Exception("AdService has not been initialized.");
        var adData = $"sid:{sessionId};ad:{Guid.NewGuid()}";
        await Services.AdService.ShowAd(RequiredUiContext, adData, cancellationToken).VhConfigureAwait();
        return adData;
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
        if (_isDisconnecting || IsIdle)
            return;

        try
        {
            if (byUser)
            {
                VhLogger.Instance.LogTrace("User requests disconnection.");
                _hasDisconnectedByUser = true;
            }

            _isDisconnecting = true;
            FireConnectionStateChanged();

            // check diagnose
            if (_hasDiagnoseStarted && _appPersistState.LastErrorMessage == null)
                _appPersistState.LastErrorMessage = "Diagnoser has finished and no issue has been detected.";

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
            VhLogger.Instance.LogError(ex, "Error in disconnecting.");
        }
        finally
        {
            _appPersistState.LastErrorMessage ??= LastSessionStatus?.ErrorMessage;
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
        using var asyncLock = await AsyncLock.LockAsync("GetIpGroupManager").VhConfigureAwait();
        if (_ipGroupManager != null)
            return _ipGroupManager;

        // AddFromIp2Location if hash has been changed
        try
        {
            var ipGroupManager = await IpGroupManager.Create(IpGroupsFolderPath).VhConfigureAwait();
            
            // ignore country ip groups if not required usually by tests
            if (!_useIpGroupManager)
            {
                _ipGroupManager = ipGroupManager;
                return _ipGroupManager;
            }

            _isLoadingIpGroup = true;
            FireConnectionStateChanged();
            await using var memZipStream = new MemoryStream(App.Resource.IP2LOCATION_LITE_DB1_IPV6_CSV);
            using var zipArchive = new ZipArchive(memZipStream);
            var entry = zipArchive.GetEntry("IP2LOCATION-LITE-DB1.IPV6.CSV") ??
                        throw new Exception("Could not find ip2location database.");
            
            await ipGroupManager.InitByIp2LocationZipStream(entry).VhConfigureAwait();
            _ipGroupManager = ipGroupManager;
            return ipGroupManager;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not load ip location database.");
            if (!UserSettings.TunnelClientCountry)
            {
                UserSettings.TunnelClientCountry = true;
                Settings.Save();
            }
            
            throw new Exception($"Could not load ip location database so I can not exclude your country. Message: {ex.Message}", ex);
        }
        finally
        {
            _isLoadingIpGroup = false;
            FireConnectionStateChanged();
        }
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

    public async Task VersionCheck(bool force = false)
    {
        if (!force && _appPersistState.UpdateIgnoreTime + _versionCheckInterval > DateTime.Now)
            return;

        // check version by app container
        try
        {
            if (UiContext != null && Services.UpdaterService != null && await Services.UpdaterService.Update(UiContext).VhConfigureAwait())
            {
                VersionCheckPostpone();
                return;
            }
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(ex, "Could not check version by VersionCheck.");
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
            VhLogger.Instance.LogWarning(ex, "Could not retrieve the latest publish info information.");
            return null; // could not retrieve the latest publish info. try later
        }
    }

    public async Task<IpRange[]?> GetIncludeIpRanges(IPAddress clientIp)
    {
        // calculate packetCaptureIpRanges
        var ipRanges = IpNetwork.All.ToIpRanges();
        if (!VhUtil.IsNullOrEmpty(UserSettings.IncludeIpRanges)) ipRanges = ipRanges.Intersect(UserSettings.IncludeIpRanges);
        if (!VhUtil.IsNullOrEmpty(UserSettings.ExcludeIpRanges)) ipRanges = ipRanges.Exclude(UserSettings.ExcludeIpRanges);

        // exclude client country IPs
        if (!UserSettings.TunnelClientCountry)
        {
            var ipGroupManager = await GetIpGroupManager().VhConfigureAwait();
            var ipGroup = await ipGroupManager.FindIpGroup(clientIp, _appPersistState.ClientCountryCode).VhConfigureAwait();
            _appPersistState.ClientCountryCode = ipGroup?.IpGroupId;
            VhLogger.Instance.LogInformation("Client Country is: {Country}", _appPersistState.ClientCountryName);
            if (ipGroup != null)
                ipRanges = ipRanges.Exclude(await ipGroupManager.GetIpRanges(ipGroup.IpGroupId).VhConfigureAwait());
        }

        return ipRanges.ToArray();
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

    public Task RunJob()
    {
        return VersionCheck();
    }

    public void UpdateUi()
    {
        UiHasChanged?.Invoke(this, EventArgs.Empty);
        InitCulture();
    }
}
