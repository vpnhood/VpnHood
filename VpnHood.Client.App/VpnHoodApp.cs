using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Client.App.Settings;
using VpnHood.Client.Device;
using VpnHood.Client.Diagnosing;
using VpnHood.Common;
using VpnHood.Common.JobController;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Client.App;

public class VpnHoodApp : IAsyncDisposable, IIpRangeProvider, IJob
{
    private const string FileNameLog = "log.txt";
    private const string FileNameSettings = "settings.json";
    private const string FolderNameProfileStore = "profiles";
    private static VpnHoodApp? _instance;
    private readonly IAppProvider _clientAppProvider;
    private readonly SocketFactory? _socketFactory;
    private bool _hasAnyDataArrived;
    private bool _hasConnectRequested;
    private bool _hasDiagnoseStarted;
    private bool _hasDisconnectedByUser;
    private IpGroupManager? _ipGroupManager;
    private bool _isConnecting;
    private bool _isDisconnecting;
    private SessionStatus? _lastSessionStatus;
    private Exception? _lastException;
    private IpGroup? _lastCountryIpGroup;
    private AppConnectionState _lastConnectionState;
    private VpnHoodClient? Client => ClientConnect?.Client;
    private SessionStatus? LastSessionStatus => Client?.SessionStatus ?? _lastSessionStatus;
    private string? LastError => _lastException?.Message ?? LastSessionStatus?.ErrorMessage;
    private string TempFolderPath => Path.Combine(AppDataFolderPath, "Temp");
    private string IpGroupsFolderPath => Path.Combine(TempFolderPath, "ipgroups");
    
    public VersionStatus VersionStatus { get; private set; } = VersionStatus.Unknown;

    public event EventHandler? ConnectionStateChanged;
    public bool IsWaitingForAd { get; set; }
    public bool IsIdle => ConnectionState == AppConnectionState.None;
    public VpnHoodConnect? ClientConnect { get; private set; }
    public TimeSpan SessionTimeout { get; set; }
    public Diagnoser Diagnoser { get; set; } = new();
    public ClientProfile? ActiveClientProfile { get; private set; }
    public Guid LastActiveClientProfileId { get; private set; }
    public static VpnHoodApp Instance => _instance ?? throw new InvalidOperationException($"{nameof(VpnHoodApp)} has not been initialized yet!");
    public static bool IsInit => _instance != null;
    public string AppDataFolderPath { get; }
    public AppSettings Settings { get; }
    public UserSettings UserSettings => Settings.UserSettings;
    public AppFeatures Features { get; }
    public ClientProfileStore ClientProfileStore { get; }
    public IDevice Device => _clientAppProvider.Device;
    public PublishInfo? LatestPublishInfo { get; private set; }
    public JobSection JobSection { get; }
    public TimeSpan TcpTimeout { get; set; } = new ClientOptions().ConnectTimeout;
    public AppLogService LogService { get; }

    private VpnHoodApp(IAppProvider clientAppProvider, AppOptions? options = default)
    {
        if (IsInit) throw new InvalidOperationException($"{VhLogger.FormatType(this)} is already initialized.");
        options ??= new AppOptions();
        Directory.CreateDirectory(options.AppDataPath); //make sure directory exists

        _clientAppProvider = clientAppProvider ?? throw new ArgumentNullException(nameof(clientAppProvider));
        if (_clientAppProvider.Device == null) throw new ArgumentNullException(nameof(_clientAppProvider.Device));
        Device.OnStartAsService += Device_OnStartAsService;

        AppDataFolderPath = options.AppDataPath ?? throw new ArgumentNullException(nameof(options.AppDataPath));
        Settings = AppSettings.Load(Path.Combine(AppDataFolderPath, FileNameSettings));
        Settings.OnSaved += Settings_OnSaved;
        ClientProfileStore = new ClientProfileStore(Path.Combine(AppDataFolderPath, FolderNameProfileStore));
        Features = new AppFeatures();
        SessionTimeout = options.SessionTimeout;
        _socketFactory = options.SocketFactory;
        Diagnoser.StateChanged += (_, _) => CheckConnectionStateChanged();
        JobSection = new JobSection(options.UpdateCheckerInterval);
        LogService = new AppLogService(Path.Combine(AppDataFolderPath, FileNameLog));

        // create start up logger
        if (!options.IsLogToConsoleSupported) UserSettings.Logging.LogToConsole = false;
        LogService.Start(Settings.UserSettings.Logging, false);

        // add default test public server if not added yet
        RemoveClientProfileByTokenId(Guid.Parse("1047359c-a107-4e49-8425-c004c41ffb8f")); //old one; deprecated in version v2.0.261 and upper
        if (Settings.TestServerTokenAutoAdded != Settings.TestServerAccessKey)
        {
            ClientProfileStore.AddAccessKey(Settings.TestServerAccessKey);
            Settings.TestServerTokenAutoAdded = Settings.TestServerAccessKey;
        }

        Features.TestServerTokenId = Token.FromAccessKey(Settings.TestServerAccessKey).TokenId;
        Features.IsExcludeAppsSupported = Device.IsExcludeAppsSupported;
        Features.IsIncludeAppsSupported = Device.IsIncludeAppsSupported;
        Features.UpdateInfoUrl = options.UpdateInfoUrl;
        _ = CheckNewVersion();

        _instance = this;
        JobRunner.Default.Add(this);
    }

    public AppState State => new()
    {
        ConnectionState = ConnectionState,
        IsIdle = IsIdle,
        ActiveClientProfileId = ActiveClientProfile?.ClientProfileId,
        DefaultClientProfileId = DefaultClientProfileId,
        LastActiveClientProfileId = LastActiveClientProfileId,
        LogExists = IsIdle && File.Exists(LogService.LogFilePath),
        LastError = LastError,
        HasDiagnoseStarted = _hasDiagnoseStarted,
        HasDisconnectedByUser = _hasDisconnectedByUser,
        HasProblemDetected = _hasConnectRequested && IsIdle && (_hasDiagnoseStarted || LastError != null),
        SessionStatus = LastSessionStatus,
        Speed = Client?.Stat.Speed ?? new Traffic(),
        AccountTraffic = Client?.Stat.AccountTraffic ?? new Traffic(),
        SessionTraffic = Client?.Stat.SessionTraffic ?? new Traffic(),
        ClientIpGroup = _lastCountryIpGroup,
        IsWaitingForAd = IsWaitingForAd,
        VersionStatus = VersionStatus,
        LastPublishInfo = VersionStatus is VersionStatus.Deprecated or VersionStatus.Old ? LatestPublishInfo : null
    };

    private Guid? DefaultClientProfileId
    {
        get
        {
            return ClientProfileStore.ClientProfileItems.Any(x =>
                x.ClientProfile.ClientProfileId == UserSettings.DefaultClientProfileId)
                ? UserSettings.DefaultClientProfileId
                : ClientProfileStore.ClientProfileItems.FirstOrDefault()?.ClientProfile.ClientProfileId;
        }
        set
        {
            if (UserSettings.DefaultClientProfileId == value)
                return;

            UserSettings.DefaultClientProfileId = value;
            Settings.Save();
        }
    }

    public AppConnectionState ConnectionState
    {
        get
        {
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
        var connectionState = ConnectionState;
        if (connectionState == _lastConnectionState)
            return;
        _lastConnectionState = connectionState;

        // Check new version after connection
        if (VersionStatus == VersionStatus.Unknown)
            _ = CheckNewVersion();

        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (_instance == null) return;
        Settings.Save();
        await Disconnect();
        _instance = null;
    }

    public event EventHandler? ClientConnectCreated;

    public static VpnHoodApp Init(IAppProvider clientAppProvider, AppOptions? options = default)
    {
        return new VpnHoodApp(clientAppProvider, options);
    }

    private void RemoveClientProfileByTokenId(Guid guid)
    {
        var clientProfile = ClientProfileStore.ClientProfiles.FirstOrDefault(x => x.TokenId == guid);
        if (clientProfile == null) return;
        ClientProfileStore.RemoveClientProfile(clientProfile.ClientProfileId);
    }

    private void Device_OnStartAsService(object sender, EventArgs e)
    {
        var clientProfile =
            (ClientProfileStore.ClientProfiles.FirstOrDefault(x => x.ClientProfileId == UserSettings.DefaultClientProfileId)
            ?? ClientProfileStore.ClientProfiles.FirstOrDefault())
            ?? throw new Exception("There is no default configuration!");

        _ = Connect(clientProfile.ClientProfileId);
    }

    public void ClearLastError()
    {
        if (!IsIdle)
            return; //can just set in Idle State

        _lastException = null;
        _hasDiagnoseStarted = false;
        _hasDisconnectedByUser = false;
        _hasConnectRequested = false;
        _hasAnyDataArrived = false;
    }

    public async Task Connect(Guid clientProfileId, bool diagnose = false, string? userAgent = default)
    {
        // disconnect if user request diagnosing
        if (ActiveClientProfile != null && ActiveClientProfile.ClientProfileId != clientProfileId ||
            !IsIdle && diagnose && !_hasDiagnoseStarted)
            await Disconnect(true);

        // check already in progress
        if (ActiveClientProfile != null || !IsIdle)
        {
            var ex = new InvalidOperationException("Connection is already in progress!");
            VhLogger.Instance.LogError(ex.Message);
            _lastException = ex;
            throw ex;
        }

        try
        {
            // prepare logger
            ClearLastError();
            _isConnecting = true;
            _hasConnectRequested = true;
            _hasDiagnoseStarted = diagnose;
            CheckConnectionStateChanged();
            LogService.Start(Settings.UserSettings.Logging, diagnose);

            // dump user settings
            VhLogger.Instance.LogInformation("UserSettings: {UserSettings}",
                JsonSerializer.Serialize(UserSettings, new JsonSerializerOptions { WriteIndented = true }));

            // Set ActiveProfile
            ActiveClientProfile = ClientProfileStore.ClientProfiles.First(x => x.ClientProfileId == clientProfileId);
            DefaultClientProfileId = ActiveClientProfile.ClientProfileId;
            LastActiveClientProfileId = ActiveClientProfile.ClientProfileId;

            // create packet capture
            var packetCapture = await Device.CreatePacketCapture();
            if (packetCapture.IsMtuSupported)
                packetCapture.Mtu = TunnelDefaults.MtuWithoutFragmentation;

            // App filters
            if (packetCapture.CanExcludeApps && UserSettings.AppFiltersMode == FilterMode.Exclude)
                packetCapture.ExcludeApps = UserSettings.AppFilters;
            if (packetCapture.CanIncludeApps && UserSettings.AppFiltersMode == FilterMode.Include)
                packetCapture.IncludeApps = UserSettings.AppFilters;

            // connect
            await ConnectInternal(packetCapture, ActiveClientProfile.TokenId, userAgent);
        }
        catch (Exception ex)
        {
            //user may disconnect before connection closed
            if (!_hasDisconnectedByUser)
            {
                VhLogger.Instance.LogError(ex.Message);
                _lastException = ex;
            }

            await Disconnect();
            throw;
        }
        finally
        {
            _isConnecting = false;
            CheckConnectionStateChanged();
        }
    }

    private void Settings_OnSaved(object sender, EventArgs e)
    {
        if (Client == null) return;
        Client.UseUdpChannel = UserSettings.UseUdpChannel;
        Client.DropUdpPackets = UserSettings.DropUdpPackets;
    }

    private async Task<string?> GetClientCountry()
    {
        try
        {
            var ipAddress =
                await IPAddressUtil.GetPublicIpAddress(System.Net.Sockets.AddressFamily.InterNetwork) ??
                await IPAddressUtil.GetPublicIpAddress(System.Net.Sockets.AddressFamily.InterNetworkV6);

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

    private async Task ConnectInternal(IPacketCapture packetCapture, Guid tokenId, string? userAgent)
    {
        packetCapture.OnStopped += PacketCapture_OnStopped;

        // log general info
        VhLogger.Instance.LogInformation($"AppVersion: {GetType().Assembly.GetName().Version}");
        VhLogger.Instance.LogInformation($"Time: {DateTime.UtcNow.ToString("u", new CultureInfo("en-US"))}");
        VhLogger.Instance.LogInformation($"OS: {Device.OperatingSystemInfo}");
        VhLogger.Instance.LogInformation($"UserAgent: {userAgent}");
        if (_hasDiagnoseStarted)
            VhLogger.Instance.LogInformation($"Country: {await GetClientCountry()}");

        // get token
        var token = ClientProfileStore.GetToken(tokenId, true);
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
            DropUdpPackets = UserSettings.DropUdpPackets
        };
        if (_socketFactory != null) clientOptions.SocketFactory = _socketFactory;
        if (userAgent != null) clientOptions.UserAgent = userAgent;

        // Create Client
        ClientConnect = new VpnHoodConnect(
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
        ClientConnect.StateChanged += ClientConnect_StateChanged;

        try
        {
            if (_hasDiagnoseStarted)
                await Diagnoser.Diagnose(ClientConnect);
            else
                await Diagnoser.Connect(ClientConnect);
        }
        finally
        {
            // update token after connection established or if error occurred
            _ = ClientProfileStore.UpdateTokenFromUrl(token);
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
                    ipRanges.AddRange(UserSettings.CustomIpRanges ?? Array.Empty<IpRange>());
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
        if (_isDisconnecting)
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
            if (Client != null)
            {
                _hasAnyDataArrived = Client.Stat.SessionTraffic.Received > 1000;
                if (LastError == null && !_hasAnyDataArrived && UserSettings is { IpGroupFiltersMode: FilterMode.All, TunnelClientCountry: true })
                    _lastException = new Exception("No data has arrived!");
            }

            // check diagnose
            if (_hasDiagnoseStarted && LastError == null)
                _lastException = new Exception("Diagnose has finished and no issue has been detected.");

            // close client
            try
            {
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
            ActiveClientProfile = null;
            _lastSessionStatus = Client?.SessionStatus;
            _isConnecting = false;
            _isDisconnecting = false;
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

        // AddFromIp2Location if hash has been changed
        await using var memZipStream = new MemoryStream(Resource.IP2LOCATION_LITE_DB1_IPV6_CSV);
        using var zipArchive = new ZipArchive(memZipStream);
        var entry = zipArchive.GetEntry("IP2LOCATION-LITE-DB1.IPV6.CSV") ?? throw new Exception("Could not find ip2location database.");
        _ipGroupManager = await IpGroupManager.Create(IpGroupsFolderPath);
        await _ipGroupManager.InitByIp2LocationZipStream(entry);
        return _ipGroupManager;
    }

    public async Task CheckNewVersion()
    {
        if (Features.UpdateInfoUrl == null)
            return;

        try
        {
            VhLogger.Instance.LogTrace("Retrieving the latest publish info...");

            using var httpClient = new HttpClient();
            var publishInfoJson = await httpClient.GetStringAsync(Features.UpdateInfoUrl);
            LatestPublishInfo = VhUtil.JsonDeserialize<PublishInfo>(publishInfoJson);

            // Check version
            if (LatestPublishInfo.Version == null)
                throw new Exception("Version is not available in publish info.");

            // set default notification delay
            if (Features.Version <= LatestPublishInfo.DeprecatedVersion)
                VersionStatus = VersionStatus.Deprecated;

            else if (Features.Version < LatestPublishInfo.Version &&
                     DateTime.UtcNow - LatestPublishInfo.ReleaseDate > LatestPublishInfo.NotificationDelay)
                VersionStatus = VersionStatus.Old;

            else
                VersionStatus = VersionStatus.Latest;

            VhLogger.Instance.LogInformation("The latest publish info has been retrieved. VersionStatus: {VersionStatus}, LatestVersion: {LatestVersion}",
                VersionStatus, LatestPublishInfo.Version);
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(ex, "Could not retrieve the latest publish info information.");
        }
    }

    public async Task<IpRange[]?> GetIncludeIpRanges(IPAddress clientIp)
    {
        var ipGroupManager = await GetIpGroupManager();
        _lastCountryIpGroup = await ipGroupManager.FindIpGroup(clientIp, Settings.LastCountryIpGroupId);
        Settings.LastCountryIpGroupId = _lastCountryIpGroup?.IpGroupId;
        VhLogger.Instance.LogInformation($"Client Country is: {_lastCountryIpGroup?.IpGroupName}");

        // use TunnelMyCountry
        if (!UserSettings.TunnelClientCountry)
            return _lastCountryIpGroup != null ? await GetIncludeIpRanges(FilterMode.Exclude, new[] { _lastCountryIpGroup.IpGroupId }) : null;

        // use advanced options
        return await GetIncludeIpRanges(UserSettings.IpGroupFiltersMode, UserSettings.IpGroupFilters);
    }

    public Task RunJob()
    {
        VersionStatus = VersionStatus.Unknown;
        return CheckNewVersion();
    }

}