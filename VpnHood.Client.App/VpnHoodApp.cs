using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Client.Device;
using VpnHood.Client.Diagnosing;
using VpnHood.Common;
using VpnHood.Common.JobController;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;

namespace VpnHood.Client.App;

public class VpnHoodApp : IAsyncDisposable, IIpFilter, IJob
{
    private const string FileNameLog = "log.txt";
    private const string FileNameSettings = "settings.json";
    private const string FileNameIpGroups = "ipgroups.json";
    private const string FolderNameProfileStore = "profiles";
    private static VpnHoodApp? _instance;
    private readonly IAppProvider _clientAppProvider;
    private readonly bool _logToConsole;
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
    private StreamLogger? _streamLogger;
    private IpGroup? _lastClientIpGroup;
    private AppConnectionState _lastConnectionState;
    private VpnHoodClient? Client => ClientConnect?.Client;
    private SessionStatus? LastSessionStatus => Client?.SessionStatus ?? _lastSessionStatus;
    private string? LastError => _lastException?.Message ?? LastSessionStatus?.ErrorMessage;

    public VersionStatus VersionStatus { get; private set; } = VersionStatus.Unknown;

    public event EventHandler? ConnectionStateChanged;
    public bool IsWaitingForAd { get; set; }
    public bool IsIdle => ConnectionState == AppConnectionState.None;
    public VpnHoodConnect? ClientConnect { get; private set; }
    public TimeSpan SessionTimeout { get; set; }
    public Diagnoser Diagnoser { get; set; } = new();
    public ClientProfile? ActiveClientProfile { get; private set; }
    public Guid LastActiveClientProfileId { get; private set; }
    public bool LogAnonymous { get; }
    public static VpnHoodApp Instance => _instance ?? throw new InvalidOperationException($"{nameof(VpnHoodApp)} has not been initialized yet!");
    public static bool IsInit => _instance != null;
    public string AppDataFolderPath { get; }
    public string LogFilePath => Path.Combine(AppDataFolderPath, FileNameLog);
    public AppSettings Settings { get; }
    public UserSettings UserSettings => Settings.UserSettings;
    public AppFeatures Features { get; }
    public ClientProfileStore ClientProfileStore { get; }
    public IDevice Device => _clientAppProvider.Device;
    public PublishInfo? LatestPublishInfo { get; private set; }
    public JobSection? JobSection { get; }
    
    private VpnHoodApp(IAppProvider clientAppProvider, AppOptions? options = default)
    {
        if (IsInit) throw new InvalidOperationException($"{VhLogger.FormatTypeName(this)} is already initialized.");
        options ??= new AppOptions();
        Directory.CreateDirectory(options.AppDataPath); //make sure directory exists

        _clientAppProvider = clientAppProvider ?? throw new ArgumentNullException(nameof(clientAppProvider));
        if (_clientAppProvider.Device == null) throw new ArgumentNullException(nameof(_clientAppProvider.Device));
        Device.OnStartAsService += Device_OnStartAsService;

        _logToConsole = options.LogToConsole;
        AppDataFolderPath = options.AppDataPath ?? throw new ArgumentNullException(nameof(options.AppDataPath));
        Settings = AppSettings.Load(Path.Combine(AppDataFolderPath, FileNameSettings));
        Settings.OnSaved += Settings_OnSaved;
        ClientProfileStore = new ClientProfileStore(Path.Combine(AppDataFolderPath, FolderNameProfileStore));
        Features = new AppFeatures();
        SessionTimeout = options.SessionTimeout;
        _socketFactory = options.SocketFactory;
        Diagnoser.StateChanged += (_, _) => CheckConnectionStateChanged();
        JobSection = new JobSection(options.UpdateCheckerInterval);

        // create default logger
        LogAnonymous = options.LogAnonymous;
        VhLogger.IsAnonymousMode = options.LogAnonymous;
        VhLogger.Instance = CreateLogger(false);

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
        LogExists = IsIdle && File.Exists(LogFilePath),
        LastError = LastError,
        HasDiagnoseStarted = _hasDiagnoseStarted,
        HasDisconnectedByUser = _hasDisconnectedByUser,
        HasProblemDetected = _hasConnectRequested && IsIdle && (_hasDiagnoseStarted || LastError != null),
        SessionStatus = LastSessionStatus,
        ReceiveSpeed = Client?.ReceiveSpeed ?? 0,
        ReceivedTraffic = Client?.ReceivedByteCount ?? 0,
        SendSpeed = Client?.SendSpeed ?? 0,
        SentTraffic = Client?.SentByteCount ?? 0,
        ClientIpGroup = _lastClientIpGroup,
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
            ClientProfileStore.ClientProfiles.FirstOrDefault(x =>
                x.ClientProfileId == UserSettings.DefaultClientProfileId) ?? ClientProfileStore.ClientProfiles.FirstOrDefault();
        if (clientProfile == null) throw new Exception("There is no default configuration!");

        _ = Connect(clientProfile.ClientProfileId);
    }

    public string GetLogForReport()
    {
        var log = File.ReadAllText(LogFilePath);
        return log;
    }

    private ILogger CreateLogger(bool addFileLogger)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            // console
            if (_logToConsole)
                builder.AddSimpleConsole(configure =>
                {
                    configure.TimestampFormat = "[HH:mm:ss.ffff] ";
                    configure.IncludeScopes = true;
                    configure.SingleLine = false;
                });

            // file logger, close old stream
            _streamLogger?.Dispose();
            _streamLogger = null;
            if (addFileLogger)
            {
                var fileStream = new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _streamLogger = new StreamLogger(fileStream);
                builder.AddProvider(_streamLogger);
            }

            builder.SetMinimumLevel(Settings.UserSettings.LogVerbose ? LogLevel.Trace : LogLevel.Information);
        });

        var logger = loggerFactory.CreateLogger("");
        return new SyncLogger(logger);
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
            VhLogger.IsDiagnoseMode |= diagnose; // never disable VhLogger.IsDiagnoseMode
            CheckConnectionStateChanged();

            if (File.Exists(LogFilePath)) File.Delete(LogFilePath);
            var logger = CreateLogger(diagnose || Settings.UserSettings.LogToFile);
            VhLogger.Instance = new FilterLogger(logger, eventId =>
            {
                if (eventId == GeneralEventId.Session) return true;
                if (eventId == GeneralEventId.Tcp) return VhLogger.IsDiagnoseMode;
                if (eventId == GeneralEventId.Ping) return VhLogger.IsDiagnoseMode;
                if (eventId == GeneralEventId.Nat) return VhLogger.IsDiagnoseMode;
                if (eventId == GeneralEventId.Dns) return VhLogger.IsDiagnoseMode;
                if (eventId == GeneralEventId.Udp) return VhLogger.IsDiagnoseMode;
                if (eventId == GeneralEventId.TcpProxyChannel) return VhLogger.IsDiagnoseMode;
                if (eventId == GeneralEventId.DatagramChannel) return true;
                return true;
            });

            // Set ActiveProfile
            ActiveClientProfile = ClientProfileStore.ClientProfiles.First(x => x.ClientProfileId == clientProfileId);
            DefaultClientProfileId = ActiveClientProfile.ClientProfileId;
            LastActiveClientProfileId = ActiveClientProfile.ClientProfileId;

            // create packet capture
            var packetCapture = await Device.CreatePacketCapture();
            if (packetCapture.IsMtuSupported)
                packetCapture.Mtu = TunnelUtil.MtuWithoutFragmentation;

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
        if (Client != null)
            Client.UseUdpChannel = UserSettings.UseUdpChannel;
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
            var ipGroup = await ipGroupManager.FindIpGroup(ipAddress);
            return ipGroup?.IpGroupName;
        }
        catch
        {
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

        // create clientOptions
        var clientOptions = new ClientOptions
        {
            SessionTimeout = SessionTimeout,
            ExcludeLocalNetwork = UserSettings.ExcludeLocalNetwork,
            IpFilter = this,
            PacketCaptureIncludeIpRanges = GetIncludeIpRanges(UserSettings.PacketCaptureIpRangesFilterMode, UserSettings.PacketCaptureIpRanges),
            MaxDatagramChannelCount = UserSettings.MaxDatagramChannelCount
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
                UdpChannelMode = UserSettings.UseUdpChannel ? UdpChannelMode.On : UdpChannelMode.Off
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

    private IpRange[]? GetIncludeIpRanges(FilterMode filterMode, IpRange[]? ipRanges)
    {
        if (filterMode == FilterMode.All || Util.IsNullOrEmpty(ipRanges))
            return null;

        if (filterMode == FilterMode.Include)
            return ipRanges;

        return IpRange.Invert(ipRanges).ToArray();
    }

    private async Task<IpRange[]?> GetIncludeIpRanges(FilterMode filterMode, string[]? ipGroupIds)
    {
        if (filterMode == FilterMode.All || Util.IsNullOrEmpty(ipGroupIds))
            return null;

        if (filterMode == FilterMode.Include)
            return await GetIpRanges(ipGroupIds);

        return IpRange.Invert(await GetIpRanges(ipGroupIds)).ToArray();
    }

    private async Task<IpRange[]> GetIpRanges(string[] ipGroupIds)
    {
        List<IpRange> ipRanges = new();
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
                _hasAnyDataArrived = Client.ReceivedByteCount > 1000;
                if (LastError == null && !_hasAnyDataArrived && UserSettings is { IpGroupFiltersMode: FilterMode.All, TunnelClientCountry: true })
                    _lastException = new Exception("No data has arrived!");
            }

            // check diagnose
            if (_hasDiagnoseStarted && LastError == null)
                _lastException = new Exception("Diagnose has finished and no issue has been detected.");

            // close client
            try
            {
                if (ClientConnect != null)
                    await ClientConnect.DisposeAsync();
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError($"Could not dispose client properly! Error: {ex}");
            }

            VhLogger.Instance = CreateLogger(false);
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
        //var customIpGroup = new IpGroup { IpGroupId = "custom", IpGroupName = "Custom" };
        //return ipGroupManager.IpGroups.Concat(new[] { customIpGroup }).ToArray();
        return ipGroupManager.IpGroups;
    }

    private async Task<IpGroupManager> GetIpGroupManager()
    {
        if (_ipGroupManager != null)
            return _ipGroupManager;

        var ipGroupsPath = Path.Combine(AppDataFolderPath, "Temp", "ipgroups");

        // AddFromIp2Location if hash has been changed
        await using var memZipStream = new MemoryStream(Resource.IP2LOCATION_LITE_DB1_CSV);
        memZipStream.Seek(0, SeekOrigin.Begin);
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(memZipStream);
        var hashString = BitConverter.ToString(hash).Replace("-", "");
        var path = Path.Combine(ipGroupsPath, hashString);

        // create
        _ipGroupManager = new IpGroupManager(Path.Combine(path, FileNameIpGroups));
        if (!Directory.Exists(path))
        {
            try
            {
                Directory.Delete(ipGroupsPath, true);
            }
            catch
            {
                // ignored
            }

            memZipStream.Seek(0, SeekOrigin.Begin);
            using var zipArchive = new ZipArchive(memZipStream);
            var entry = zipArchive.GetEntry("IP2LOCATION-LITE-DB1.CSV") ?? throw new Exception("Could not find ip2location database!");
            await using var stream = entry.Open();
            await _ipGroupManager.AddFromIp2Location(stream);
        }

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
            LatestPublishInfo = Util.JsonDeserialize<PublishInfo>(publishInfoJson);

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
        var ipGroup = await ipGroupManager.FindIpGroup(clientIp);
        _lastClientIpGroup = ipGroup;
        VhLogger.Instance.LogInformation($"Client Country is: {ipGroup?.IpGroupName}");

        // use TunnelMyCountry
        if (!UserSettings.TunnelClientCountry)
            return ipGroup != null ? await GetIncludeIpRanges(FilterMode.Exclude, new[] { ipGroup.IpGroupId }) : null;

        // use advanced options
        return await GetIncludeIpRanges(UserSettings.IpGroupFiltersMode, UserSettings.IpGroupFilters);
    }

    public Task RunJob()
    {
        VersionStatus = VersionStatus.Unknown;
        return CheckNewVersion();
    }

}