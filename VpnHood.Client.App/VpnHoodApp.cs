using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Exceptions;
using VpnHood.Client.App.Settings;
using VpnHood.Client.Device;
using VpnHood.Client.Diagnosing;
using VpnHood.Common;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Jobs;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Client.App;

public class VpnHoodApp : Singleton<VpnHoodApp>, IAsyncDisposable, IIpRangeProvider, IJob
{
    private const string FileNameLog = "log.txt";
    private const string FileNameSettings = "settings.json";
    private const string FolderNameProfiles = "profiles";
    private readonly SocketFactory? _socketFactory;
    private readonly bool _loadCountryIpGroups;
    private readonly string? _appGa4MeasurementId;
    private bool _hasAnyDataArrived;
    private bool _hasConnectRequested;
    private bool _hasDiagnoseStarted;
    private bool _hasDisconnectedByUser;
    private Guid? _activeClientProfileId;
    private DateTime? _connectRequestTime;
    private IpGroupManager? _ipGroupManager;
    private bool _isConnecting;
    private bool _isDisconnecting;
    private SessionStatus? _lastSessionStatus;
    private string? _lastError;
    private IpGroup? _lastCountryIpGroup;
    private AppConnectionState _lastConnectionState;
    private int _initializingState;
    private readonly TimeSpan _versionCheckInterval;

    private VpnHoodClient? Client => ClientConnect?.Client;
    private SessionStatus? LastSessionStatus => Client?.SessionStatus ?? _lastSessionStatus;
    private string TempFolderPath => Path.Combine(AppDataFolderPath, "Temp");
    private string IpGroupsFolderPath => Path.Combine(TempFolderPath, "ipgroups");
    private VersionStatus _versionStatus = VersionStatus.Unknown;
    private CancellationTokenSource? _connectCts;
    private DateTime? _connectedTime;
    private ClientProfile? _clientProfile;

    public bool VersionCheckRequired { get; private set; }
    public event EventHandler? ConnectionStateChanged;
    public event EventHandler? UiHasChanged;
    public bool IsWaitingForAd { get; private set; }
    public bool IsIdle => ConnectionState == AppConnectionState.None;
    public VpnHoodConnect? ClientConnect { get; private set; }
    public TimeSpan SessionTimeout { get; set; }
    public Diagnoser Diagnoser { get; set; } = new();
    public string AppDataFolderPath { get; }
    public AppSettings Settings { get; }
    public UserSettings UserSettings => Settings.UserSettings;
    public AppFeatures Features { get; }
    public ClientProfileService ClientProfileService { get; }
    public IDevice Device { get; }
    public PublishInfo? LatestPublishInfo { get; private set; }
    public JobSection JobSection { get; }
    public TimeSpan TcpTimeout { get; set; } = new ClientOptions().ConnectTimeout;
    public AppLogService LogService { get; }
    public AppResource Resource { get; }
    public AppServices Services { get; }

    private VpnHoodApp(IDevice device, AppOptions? options = default)
    {
        options ??= new AppOptions();
        Directory.CreateDirectory(options.AppDataFolderPath); //make sure directory exists
        Resource = options.Resource;

        Device = device;
        device.StartedAsService += DeviceOnStartedAsService;

        AppDataFolderPath = options.AppDataFolderPath ?? throw new ArgumentNullException(nameof(options.AppDataFolderPath));
        Settings = AppSettings.Load(Path.Combine(AppDataFolderPath, FileNameSettings));
        Settings.Saved += Settings_Saved;
        ClientProfileService = new ClientProfileService(Path.Combine(AppDataFolderPath, FolderNameProfiles));
        SessionTimeout = options.SessionTimeout;
        _socketFactory = options.SocketFactory;
        Diagnoser.StateChanged += (_, _) => CheckConnectionStateChanged();
        LogService = new AppLogService(Path.Combine(AppDataFolderPath, FileNameLog));
        _loadCountryIpGroups = options.LoadCountryIpGroups;
        _appGa4MeasurementId = options.AppGa4MeasurementId;
        _versionCheckInterval = options.VersionCheckInterval;

        // configure update job section
        JobSection = new JobSection(new JobOptions
        {
            Interval = options.VersionCheckInterval,
            DueTime = options.VersionCheckInterval > TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(5) : options.VersionCheckInterval,
            Name = "VersionCheck"
        });

        // create start up logger
        if (!device.IsLogToConsoleSupported) UserSettings.Logging.LogToConsole = false;
        LogService.Start(Settings.UserSettings.Logging, false);

        // add default test public server if not added yet
        ClientProfileService.Remove(Guid.Parse("5aacec55-5cac-457a-acad-3976969236f8")); //remove obsoleted public server
        foreach (var accessKey in options.AccessKeys)
        {
            var clientProfile = ClientProfileService.ImportAccessKey(accessKey);
            Settings.UserSettings.ClientProfileId ??= clientProfile.ClientProfileId; // set first access key as default
        }

        // initialize features
        Features = new AppFeatures
        {
            Version = typeof(VpnHoodApp).Assembly.GetName().Version,
            IsExcludeAppsSupported = Device.IsExcludeAppsSupported,
            IsIncludeAppsSupported = Device.IsIncludeAppsSupported,
            IsAddAccessKeySupported = options.IsAddAccessKeySupported,
            UpdateInfoUrl = options.UpdateInfoUrl,
            UiName = options.UiName,
        };

        // initialize services
        Services = new AppServices
        {
            CultureService = device.CultureService ?? new AppCultureService(this)
        };

        // initialize
        InitCulture();
        JobRunner.Default.Add(this);
    }

    public ClientProfile? ClientProfile
    {
        get
        {
            if (_clientProfile?.ClientProfileId != UserSettings.ClientProfileId)
                _clientProfile = ClientProfileService.FindById(UserSettings.ClientProfileId ?? Guid.Empty);
            return _clientProfile;
        }
    }

    public AppState State
    {
        get
        {
            var connectionState = ConnectionState;
            return new AppState
            {
                ConfigTime = Settings.ConfigTime,
                ConnectionState = connectionState,
                IsIdle = IsIdle,
                CanConnect = connectionState is AppConnectionState.None,
                CanDisconnect = !_isDisconnecting && (connectionState
                    is AppConnectionState.Connected or AppConnectionState.Connecting
                    or AppConnectionState.Diagnosing or AppConnectionState.Waiting),
                ClientProfileId = ClientProfile?.ClientProfileId,
                HostRegionId = UserSettings.HostRegionId,
                LogExists = IsIdle && File.Exists(LogService.LogFilePath),
                LastError = _lastError,
                HasDiagnoseStarted = _hasDiagnoseStarted,
                HasDisconnectedByUser = _hasDisconnectedByUser,
                HasProblemDetected = _hasConnectRequested && IsIdle && (_hasDiagnoseStarted || _lastError != null),
                SessionStatus = LastSessionStatus,
                Speed = Client?.Stat.Speed ?? new Traffic(),
                AccountTraffic = Client?.Stat.AccountTraffic ?? new Traffic(),
                SessionTraffic = Client?.Stat.SessionTraffic ?? new Traffic(),
                ClientIpGroup = _lastCountryIpGroup,
                IsWaitingForAd = IsWaitingForAd,
                VersionStatus = _versionStatus,
                LastPublishInfo = _versionStatus is VersionStatus.Deprecated or VersionStatus.Old ? LatestPublishInfo : null,
                ConnectRequestTime = _connectRequestTime,
                IsUdpChannelSupported = Client?.Stat.IsUdpChannelSupported,
                CurrentUiCultureInfo = new UiCultureInfo(CultureInfo.DefaultThreadCurrentUICulture),
                SystemUiCultureInfo = new UiCultureInfo(SystemUiCulture),
            };
        }
    }

    public AppConnectionState ConnectionState
    {
        get
        {
            if (_initializingState > 0) return AppConnectionState.Initializing;
            if (Diagnoser.IsWorking) return AppConnectionState.Diagnosing;
            if (_isDisconnecting || Client?.State == ClientState.Disconnecting) return AppConnectionState.Disconnecting;
            if (_isConnecting || Client?.State == ClientState.Connecting) return AppConnectionState.Connecting;
            if (Client?.State == ClientState.Connected && IsWaitingForAd) return AppConnectionState.Connecting;
            if (Client?.State == ClientState.Connected) return AppConnectionState.Connected;
            if (ClientConnect?.IsWaiting == true) return AppConnectionState.Waiting;
            return AppConnectionState.None;
        }
    }

    private void CheckConnectionStateChanged()
    {
        // check changed state
        var connectionState = ConnectionState;
        if (connectionState == _lastConnectionState)
            return;
        _lastConnectionState = connectionState;
        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        await Disconnect();
        Device.Dispose();
        DisposeSingleton();
    }

    public event EventHandler? ClientConnectCreated;

    public static VpnHoodApp Init(IDevice device, AppOptions? options = default)
    {
        return new VpnHoodApp(device, options);
    }

    private void DeviceOnStartedAsService(object sender, EventArgs e)
    {
        var clientProfile = ClientProfile ?? throw new Exception("There is no access key.");
        _ = Connect(clientProfile.ClientProfileId);
    }

    public void ClearLastError()
    {
        if (!IsIdle)
            return; //can just set in Idle State

        _lastError = null;
        _hasDiagnoseStarted = false;
        _hasDisconnectedByUser = false;
    }

    public async Task Connect(Guid? clientProfileId = null, string? regionId = null, bool diagnose = false,
        string? userAgent = default, bool throwException = true, CancellationToken cancellationToken = default)
    {
        // disconnect current connection
        if (!IsIdle)
            await Disconnect(true);

        // set default profileId to clientProfileId if not set
        var clientProfile = ClientProfileService.FindById(clientProfileId ?? UserSettings.ClientProfileId ?? Guid.Empty);
        if (clientProfile == null)
            throw new NotExistsException("Could not find any VPN profile to connect.");

        // set current profile
        UserSettings.ClientProfileId = clientProfile.ClientProfileId;
        Settings.Save();

        try
        {
            // prepare logger
            ClearLastError();
            _activeClientProfileId = clientProfileId;
            _isConnecting = true;
            _hasAnyDataArrived = false;
            _hasDisconnectedByUser = false;
            _hasConnectRequested = true;
            _hasDiagnoseStarted = diagnose;
            _connectRequestTime = DateTime.Now;
            IsWaitingForAd = false;
            CheckConnectionStateChanged();
            LogService.Start(Settings.UserSettings.Logging, diagnose);

            // log general info
            VhLogger.Instance.LogInformation($"AppVersion: {GetType().Assembly.GetName().Version}");
            VhLogger.Instance.LogInformation($"Time: {DateTime.UtcNow.ToString("u", new CultureInfo("en-US"))}");
            VhLogger.Instance.LogInformation($"OS: {Device.OsInfo}");
            VhLogger.Instance.LogInformation($"UserAgent: {userAgent}");
            VhLogger.Instance.LogInformation("UserSettings: {UserSettings}", JsonSerializer.Serialize(UserSettings, new JsonSerializerOptions { WriteIndented = true }));

            // it slows down tests and does not need to be logged in normal situation
            if (diagnose)
                VhLogger.Instance.LogInformation($"Country: {await GetClientCountry()}");

            VhLogger.Instance.LogInformation("VpnHood Client is Connecting ...");

            // create cancellationToken
            _connectCts = new CancellationTokenSource();
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _connectCts.Token);
            cancellationToken = linkedCts.Token;

            // create packet capture
            var packetCapture = await Device.CreatePacketCapture();
            packetCapture.Stopped += PacketCapture_OnStopped;

            // init packet capture
            if (packetCapture.IsMtuSupported)
                packetCapture.Mtu = TunnelDefaults.MtuWithoutFragmentation;

            // App filters
            if (packetCapture.CanExcludeApps && UserSettings.AppFiltersMode == FilterMode.Exclude)
                packetCapture.ExcludeApps = UserSettings.AppFilters;

            if (packetCapture.CanIncludeApps && UserSettings.AppFiltersMode == FilterMode.Include)
                packetCapture.IncludeApps = UserSettings.AppFilters;

            // connect
            await ConnectInternal(packetCapture, clientProfile.Token, regionId, userAgent, true, cancellationToken);
        }
        catch (Exception ex)
        {
            //user may disconnect before connection closed
            if (!_hasDisconnectedByUser)
            {
                VhLogger.Instance.LogError(ex.Message);
                _lastError = ex.Message;
            }

            await Disconnect();

            if (throwException)
                throw;
        }
        finally
        {
            _connectCts?.Dispose();
            _connectCts = null;
            _isConnecting = false;
            IsWaitingForAd = false;
            CheckConnectionStateChanged();
        }
    }

    public CultureInfo SystemUiCulture => new(
        Services.CultureService.SystemCultures.FirstOrDefault()?.Split("-").FirstOrDefault()
        ?? CultureInfo.InstalledUICulture.TwoLetterISOLanguageName);

    private void InitCulture()
    {
        // set default culture
        var firstSelected = Services.CultureService.SelectedCultures.FirstOrDefault();
        CultureInfo.CurrentUICulture = (firstSelected != null) ? new CultureInfo(firstSelected) : SystemUiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(Services.CultureService.SelectedCultures.FirstOrDefault() ?? "en");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;

        // sync UserSettings from the System App Settings
        UserSettings.CultureCode = firstSelected?.Split("-").FirstOrDefault();
    }

    private void Settings_Saved(object sender, EventArgs e)
    {
        if (Client != null)
        {
            Client.UseUdpChannel = UserSettings.UseUdpChannel;
            Client.DropUdpPackets = UserSettings.DropUdpPackets;
            if (!IsIdle && UserSettings.ClientProfileId != _activeClientProfileId)
                _ = Disconnect(true);
        }

        // sync culture to app settings
        _clientProfile = null; //lets refresh it
        Services.CultureService.SelectedCultures = UserSettings.CultureCode != null ? [UserSettings.CultureCode] : [];
        InitCulture();
    }

    private async Task<string?> GetClientCountry()
    {
        try
        {
            var ipAddress =
                await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetwork) ??
                await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetworkV6);

            if (ipAddress == null)
                return null;

            var ipGroupManager = await GetIpGroupManager();
            _lastCountryIpGroup = await ipGroupManager.FindIpGroup(ipAddress, Settings.LastCountryIpGroupId);
            Settings.LastCountryIpGroupId = _lastCountryIpGroup?.IpGroupId;
            return _lastCountryIpGroup?.IpGroupName;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not retrieve client country from public ip services.");
            return null;
        }
    }

    private async Task ConnectInternal(IPacketCapture packetCapture, Token token, string? regionId, string? userAgent,
        bool allowUpdateToken, CancellationToken cancellationToken)
    {
        // show token info
        VhLogger.Instance.LogInformation($"TokenId: {VhLogger.FormatId(token.TokenId)}, SupportId: {VhLogger.FormatId(token.SupportId)}");

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
            ExcludeLocalNetwork = UserSettings.ExcludeLocalNetwork,
            IpRangeProvider = this,
            PacketCaptureIncludeIpRanges = packetCaptureIpRanges.ToArray(),
            MaxDatagramChannelCount = UserSettings.MaxDatagramChannelCount,
            ConnectTimeout = TcpTimeout,
            AllowAnonymousTracker = UserSettings.AllowAnonymousTracker,
            DropUdpPackets = UserSettings.DropUdpPackets,
            AppGa4MeasurementId = _appGa4MeasurementId,
            RegionId = regionId
        };

        if (_socketFactory != null) clientOptions.SocketFactory = _socketFactory;
        if (userAgent != null) clientOptions.UserAgent = userAgent;

        // Create Client
        var clientConnect = new VpnHoodConnect(
            packetCapture,
            Settings.ClientId,
            token,
            clientOptions,
            new ConnectOptions
            {
                MaxReconnectCount = UserSettings.MaxReconnectCount,
                UdpChannelMode = UserSettings.UseUdpChannel ? UdpChannelMode.On : UdpChannelMode.Off,
            });

        ClientConnectCreated?.Invoke(this, EventArgs.Empty);
        clientConnect.StateChanged += ClientConnect_StateChanged;
        ClientConnect = clientConnect; // set here to allow disconnection

        try
        {
            if (_hasDiagnoseStarted)
                await Diagnoser.Diagnose(clientConnect, cancellationToken);
            else
                await Diagnoser.Connect(clientConnect, cancellationToken);

            // set connected time
            _connectedTime = DateTime.Now;

            // update access token if ResponseAccessKey is set
            if (clientConnect.Client.ResponseAccessKey != null)
                token = ClientProfileService.UpdateTokenByAccessKey(token, clientConnect.Client.ResponseAccessKey);

            // check version after first connection
            _ = VersionCheck();

            // Show ad if it is required and does not show yet
            if (clientConnect.Client.SessionStatus.IsAdRequired)
            {
                var adData = await ShowAd(clientConnect.Client.SessionId, cancellationToken);
                if (string.IsNullOrEmpty(adData))
                    throw new AdException("Could not display the require ad.");

                await ClientConnect.Client.SendAdReward(adData, cancellationToken);
            }
        }
        catch
        {
            // try to update token from url after connection or error if ResponseAccessKey is not set
            if (!string.IsNullOrEmpty(token.ServerToken.Url) && allowUpdateToken &&
                await ClientProfileService.UpdateTokenByUrl(token))
            {
                token = ClientProfileService.GetToken(token.TokenId);
                await ConnectInternal(packetCapture, token, regionId, userAgent, false, cancellationToken);
                return;
            }

            throw;
        }
    }

    private async Task<string?> ShowAd(ulong sessionId, CancellationToken cancellationToken)
    {
        if (Services.AdService == null)
            throw new Exception("This server requires a display ad, but AppAdService has not been initialized.");

        IsWaitingForAd = true;
        try
        {
            var customData = $"sid:{sessionId};ad:{Guid.NewGuid()}";
            await Services.AdService.ShowAd(customData, cancellationToken);
            return customData;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogInformation(ex, "Error in displaying the ad.");
            return null;
        }
        finally
        {
            IsWaitingForAd = false;
        }
    }

    private void ClientConnect_StateChanged(object sender, EventArgs e)
    {
        if (ClientConnect?.IsDisposed == true)
            _ = Disconnect();
        else
            CheckConnectionStateChanged();
    }

    private async Task<IpRange[]?> GetIncludeIpRanges(FilterMode filterMode, string[]? ipGroupIds)
    {
        if (filterMode == FilterMode.All || VhUtil.IsNullOrEmpty(ipGroupIds))
            return null;

        if (filterMode == FilterMode.Include)
            return await GetIpRanges(ipGroupIds);

        return IpRange.Invert(await GetIpRanges(ipGroupIds)).ToArray();
    }

    private async Task<IpRange[]> GetIpRanges(IEnumerable<string> ipGroupIds)
    {
        var ipRanges = new List<IpRange>();
        foreach (var ipGroupId in ipGroupIds)
            try
            {
                if (ipGroupId.Equals("custom", StringComparison.OrdinalIgnoreCase))
                    ipRanges.AddRange(UserSettings.CustomIpRanges ?? []);
                else
                {
                    var ipGroupManager = await GetIpGroupManager();
                    ipRanges.AddRange(await ipGroupManager.GetIpRanges(ipGroupId));
                }
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, $"Could not add {nameof(IpRange)} of Group {ipGroupId}");
            }

        return IpRange.Sort(ipRanges).ToArray();
    }

    private void PacketCapture_OnStopped(object sender, EventArgs e)
    {
        _ = Disconnect();
    }

    public async Task Disconnect(bool byUser = false)
    {
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
            CheckConnectionStateChanged();

            // check for any success
            if (Client != null && _connectedTime != null)
            {
                _hasAnyDataArrived = Client.Stat.SessionTraffic.Received > 1000;
                if (_lastError == null && !_hasAnyDataArrived && UserSettings is { IpGroupFiltersMode: FilterMode.All, TunnelClientCountry: true })
                    _lastError = "No data has been received.";
            }

            // check diagnose
            if (_hasDiagnoseStarted && _lastError == null)
                _lastError = "Diagnoser has finished and no issue has been detected.";

            // close client
            try
            {
                // cancel current connecting if any
                _connectCts?.Cancel();

                // do not wait for bye if user request disconnection
                if (ClientConnect != null)
                    await ClientConnect.DisposeAsync(waitForBye: !byUser);
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(GeneralEventId.Session, ex, "Could not dispose the client properly.");
            }

            LogService.Stop();
        }
        finally
        {
            _lastError ??= LastSessionStatus?.ErrorMessage;
            _activeClientProfileId = null;
            _lastSessionStatus = Client?.SessionStatus;
            _isConnecting = false;
            _isDisconnecting = false;
            _connectedTime = null;
            ClientConnect = null;
            CheckConnectionStateChanged();
        }
    }

    public async Task<IpGroup[]> GetIpGroups()
    {
        var ipGroupManager = await GetIpGroupManager();
        return await ipGroupManager.GetIpGroups();
    }

    private async Task<IpGroupManager> GetIpGroupManager()
    {
        if (_ipGroupManager != null)
            return _ipGroupManager;

        _ipGroupManager = await IpGroupManager.Create(IpGroupsFolderPath);

        // AddFromIp2Location if hash has been changed
        if (_loadCountryIpGroups)
        {
            try
            {
                _initializingState++;
                CheckConnectionStateChanged();
                await using var memZipStream = new MemoryStream(App.Resource.IP2LOCATION_LITE_DB1_IPV6_CSV);
                using var zipArchive = new ZipArchive(memZipStream);
                var entry = zipArchive.GetEntry("IP2LOCATION-LITE-DB1.IPV6.CSV") ?? throw new Exception("Could not find ip2location database.");
                await _ipGroupManager.InitByIp2LocationZipStream(entry);
            }
            finally
            {
                _initializingState--;
                CheckConnectionStateChanged();
            }
        }

        return _ipGroupManager;
    }

    public void VersionCheckPostpone()
    {
        Settings.LastUpdateCheckTime = DateTime.Now;
        VersionCheckRequired = false;
        _versionStatus = VersionStatus.Unknown;
        Settings.Save();
    }

    public async Task VersionCheck(bool force = false)
    {
        if (!force && Settings.LastUpdateCheckTime != null && Settings.LastUpdateCheckTime.Value + _versionCheckInterval > DateTime.Now)
            return;

        // check version by app container
        if (Services.UpdaterService != null)
        {
            try
            {
                if (await Services.UpdaterService.Update())
                {
                    _versionStatus = VersionStatus.Unknown; // version status is unknown when app container can do it
                    VersionCheckRequired = false;
                    Settings.LastUpdateCheckTime = DateTime.Now;
                    Settings.Save();
                    return;
                }
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogWarning(ex, "Could not check version by VersionCheckProc.");
            }
        }

        // app container should check version if possible regardless of the result of VersionCheckByUpdateInfo
        VersionCheckRequired = true;

        // check version by update info
        if (await VersionCheckByUpdateInfo())
        {
            Settings.LastUpdateCheckTime = DateTime.Now;
            Settings.Save();
        }
    }

    private async Task<bool> VersionCheckByUpdateInfo()
    {
        try
        {
            if (Features.UpdateInfoUrl == null)
                return true; // no update info url. Job done

            VhLogger.Instance.LogTrace("Retrieving the latest publish info...");

            using var httpClient = new HttpClient();
            var publishInfoJson = await httpClient.GetStringAsync(Features.UpdateInfoUrl);
            LatestPublishInfo = VhUtil.JsonDeserialize<PublishInfo>(publishInfoJson);

            // Check version
            if (LatestPublishInfo.Version == null)
                throw new Exception("Version is not available in publish info.");

            // set default notification delay
            if (Features.Version <= LatestPublishInfo.DeprecatedVersion)
                _versionStatus = VersionStatus.Deprecated;

            else if (Features.Version < LatestPublishInfo.Version &&
                     DateTime.UtcNow - LatestPublishInfo.ReleaseDate > LatestPublishInfo.NotificationDelay)
                _versionStatus = VersionStatus.Old;

            else
                _versionStatus = VersionStatus.Latest;

            VhLogger.Instance.LogInformation("The latest publish info has been retrieved. VersionStatus: {VersionStatus}, LatestVersion: {LatestVersion}",
                _versionStatus, LatestPublishInfo.Version);

            return true; // Job done
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(ex, "Could not retrieve the latest publish info information.");
            return false; // could not retrieve the latest publish info. try later
        }
    }

    public async Task<IpRange[]?> GetIncludeIpRanges(IPAddress clientIp)
    {
        // use TunnelMyCountry
        if (UserSettings.TunnelClientCountry)
            return await GetIncludeIpRanges(UserSettings.IpGroupFiltersMode, UserSettings.IpGroupFilters);

        // Exclude my country
        var ipGroupManager = await GetIpGroupManager();
        _lastCountryIpGroup = await ipGroupManager.FindIpGroup(clientIp, Settings.LastCountryIpGroupId);
        Settings.LastCountryIpGroupId = _lastCountryIpGroup?.IpGroupId;
        VhLogger.Instance.LogInformation($"Client Country is: {_lastCountryIpGroup?.IpGroupName}");

        return _lastCountryIpGroup != null
            ? await GetIncludeIpRanges(FilterMode.Exclude, [_lastCountryIpGroup.IpGroupId])
            : null;
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