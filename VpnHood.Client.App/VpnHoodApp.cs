using VpnHood.Loggers;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace VpnHood.Client.App
{

    public class VpnHoodApp : IDisposable
    {
        private const string FILENAME_Log = "log.txt";
        private const string FILENAME_Settings = "settings.json";
        private const string FOLDERNAME_ProfileStore = "profiles";
        private readonly IAppProvider _clientAppProvider;
        private static VpnHoodApp _current;
        private readonly bool _logToConsole;
        private StreamLogger _streamLogger;
        private IPacketCapture _packetCapture;
        private VpnHoodClient _client;
        private bool _hasDiagnoseStarted;
        private bool _hasDisconnectedByUser;
        private bool _hasAnyDataArrived;
        private bool _isConnecting;
        private bool _hasConnectRequested;

        public ClientProfile ActiveClientProfile { get; private set; }
        public Guid LastActiveClientProfileId { get; private set; }
        public bool LogAnonymous { get; private set; }
        public static VpnHoodApp Current => _current ?? throw new InvalidOperationException($"{nameof(VpnHoodApp)} has not been initialized yet!");
        public static bool IsInit => _current != null;
        public static VpnHoodApp Init(IAppProvider clientAppProvider, AppOptions options = null)
        {
            return new VpnHoodApp(clientAppProvider, options);
        }

        /// <summary>
        /// Force to use this logger
        /// </summary>
        public string AppDataFolderPath { get; }
        public string LogFilePath => Path.Combine(AppDataFolderPath, FILENAME_Log);

        public event EventHandler OnStateChanged;
        public AppSettings Settings { get; private set; }
        public AppUserSettings UserSettings => Settings.UserSettings;
        public AppFeatures Features { get; private set; }
        public ClientProfileStore ClientProfileStore { get; private set; }
        public Exception LastException { get; private set; }

        private VpnHoodApp(IAppProvider clientAppProvider, AppOptions options = null)
        {
            if (IsInit) throw new InvalidOperationException($"{nameof(VpnHoodApp)} is already initialized!");
            if (options == null) options = new AppOptions();
            Directory.CreateDirectory(options.AppDataPath); //make sure directory exists

            _clientAppProvider = clientAppProvider ?? throw new ArgumentNullException(nameof(clientAppProvider));
            if (_clientAppProvider.Device == null) throw new ArgumentNullException(nameof(_clientAppProvider.Device));
            clientAppProvider.Device.OnStartAsService += Device_OnStartAsService;

            _logToConsole = options.LogToConsole;
            AppDataFolderPath = options.AppDataPath ?? throw new ArgumentNullException(nameof(options.AppDataPath));
            Settings = AppSettings.Load(Path.Combine(AppDataFolderPath, FILENAME_Settings));
            ClientProfileStore = new ClientProfileStore(Path.Combine(AppDataFolderPath, FOLDERNAME_ProfileStore));
            Features = new AppFeatures();

            // create default logger
            LogAnonymous = options.LogAnonymous;
            Logger.AnonymousMode = options.LogAnonymous;
            Logger.Current = CreateLogger(false);

            // add default test public server if not added yet
            if (Settings.TestServerTokenId == null)
                Settings.TestServerTokenId = ClientProfileStore.AddAccessKey(Settings.TestServerAccessKey).TokenId;
            Features.TestServerTokenId = Settings.TestServerTokenId;

            _current = this;
        }

        private void Device_OnStartAsService(object sender, EventArgs e)
        {
            var clientPrpfile = ClientProfileStore.ClientProfiles.FirstOrDefault(x => x.ClientProfileId == UserSettings.DefaultClientProfileId);
            if (clientPrpfile == null) ClientProfileStore.ClientProfiles.FirstOrDefault();
            if (clientPrpfile == null) throw new Exception("There is no default configuation!");

            var _ = Connect(clientPrpfile.ClientProfileId);
        }

        public AppState State => new AppState()
        {
            ClientState = ClientState,
            IsIdle = IsIdle,
            ActiveClientProfileId = ActiveClientProfile?.ClientProfileId,
            LastActiveClientProfileId = LastActiveClientProfileId,
            LogExists = IsIdle && File.Exists(LogFilePath),
            LastError = _hasConnectRequested ? LastException?.Message : null,
            HasDiagnoseStarted = _hasConnectRequested && _hasDiagnoseStarted,
            HasDisconnectedByUser = _hasConnectRequested && _hasDisconnectedByUser,
            HasProblemDetected = _hasConnectRequested && IsIdle && (!_hasAnyDataArrived || _hasDiagnoseStarted || (LastException != null && !_hasDisconnectedByUser))
        };

        private ClientState ClientState
        {
            get
            {
                var state = _client?.State ?? ClientState.None;
                if ((state == ClientState.None || state == ClientState.Disposed) && _packetCapture != null)
                    state = ClientState.Disconnecting;
                
                // no dispose state for app
                if (state == ClientState.Disposed) 
                    state = ClientState.None; 

                // set connecting by App State if client has not started yet
                if (_isConnecting && state == ClientState.None) state = ClientState.Connecting;
                return state;
            }
        }

        public string GetLogForReport()
        {
            var log = File.ReadAllText(LogFilePath);

            // remove IPs
            if (LogAnonymous)
            {
                var pattern = @"(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})";
                log = Regex.Replace(log, pattern, "*.*.$3.$4");
            }

            return log;
        }

        public bool IsIdle => ClientState == ClientState.None;

        private ILogger CreateLogger(bool addFileLogger)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                // console
                if (_logToConsole)
                    builder.AddSimpleConsole((config) => { config.IncludeScopes = true; });

                // file logger, close old stream
                _streamLogger?.Dispose();
                _streamLogger = null;
                if (addFileLogger)
                {
                    var fileStream = new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                    _streamLogger = new StreamLogger(fileStream, true);
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

            LastException = null;
            _hasAnyDataArrived = false;
            _hasDiagnoseStarted = false;
            _hasDisconnectedByUser = false;
            _hasConnectRequested = false;
        }


        public async Task Connect(Guid clientProfileId, bool diagnose = false, string userAgent = null)
        {
            try
            {
                if (ActiveClientProfile != null || !IsIdle)
                    throw new InvalidOperationException("Connection is already in progress!");

                // prepare logger
                ClearLastError();
                _isConnecting = true;
                _hasConnectRequested = true;
                _hasDiagnoseStarted = diagnose;

                if (File.Exists(LogFilePath)) File.Delete(LogFilePath);
                var logger = CreateLogger(diagnose || Settings.UserSettings.LogToFile);
                Logger.Current = new FilterLogger(logger, (eventId) =>
                {
                    if (eventId == CommonEventId.Nat) return false;
                    if (eventId == ClientEventId.DnsReply || eventId == ClientEventId.DnsRequest) return false;
                    return true;
                });

                // Set ActiveProfile
                ActiveClientProfile = ClientProfileStore.ClientProfiles.First(x => x.ClientProfileId == clientProfileId);
                LastActiveClientProfileId = ActiveClientProfile.ClientProfileId;
                var packetCapture = await _clientAppProvider.Device.CreatePacketCapture();
                await ConnectInternal(packetCapture, userAgent);

                // set default ClientProfile
                if (UserSettings.DefaultClientProfileId != ActiveClientProfile.ClientProfileId)
                {
                    UserSettings.DefaultClientProfileId = ActiveClientProfile.ClientProfileId;
                    Settings.Save();
                }

            }
            catch (Exception ex)
            {
                Logger.Current?.LogError(ex.Message);
                LastException = ex;
                Disconnect();
                throw;
            }
            finally
            {
                _isConnecting = false;
            }
        }

        private async Task ConnectInternal(IPacketCapture packetCapture, string userAgent)
        {
            _packetCapture = packetCapture;
            packetCapture.OnStopped += PacketCapture_OnStopped;

            // log general info
            Logger.Current.LogInformation($"AppVersion: {typeof(VpnHoodApp).GetType().Assembly.GetName().Version.ToString().Replace('.', ',')}, Time: {DateTime.Now}");
            Logger.Current.LogInformation($"OS: {_clientAppProvider.OperatingSystemInfo}");
            Logger.Current.LogInformation($"UserAgent: {userAgent}");

            // log token info
            var token = ClientProfileStore.GetToken(ActiveClientProfile.TokenId, true);
            Logger.Current.LogInformation($"ClientProfileInfo: TokenId: {Logger.FormatId(token.TokenId)}, SupportId: {Logger.FormatId(token.SupportId)}, ServerEndPoint: {Logger.FormatDns(token.ServerEndPoint)}");

            // Create Client
            _client = new VpnHoodClient(
                packetCapture: packetCapture,
                clientId: Settings.ClientId,
                token: token,
                new ClientOptions()
                {
                    MaxReconnectCount = Settings.UserSettings.MaxReconnectCount,
                    IpResolveMode = IpResolveMode.Token,
                });

            _client.OnStateChanged += Client_OnStateChanged;
            await _client.Connect();
        }

        private void Client_OnStateChanged(object sender, EventArgs e)
        {
            OnStateChanged?.Invoke(this, e);
        }

        private void PacketCapture_OnStopped(object sender, EventArgs e)
        {
            _packetCapture.OnStopped -= PacketCapture_OnStopped; // make sure no recursive call
            Disconnect();
        }

        public void Disconnect(bool byUser = false)
        {
            if (_client == null)
                return;

            if (byUser)
                _hasDisconnectedByUser = true;

            // check for any success
            if (_client.ReceivedByteCount > 1000)
                _hasAnyDataArrived = true;
            else if (LastException == null)
                LastException = new Exception("No data has been arrived!");

            ActiveClientProfile = null;

            // close client
            _client?.Dispose();
            _client = null;

            // close packet capture
            if (_packetCapture != null)
            {
                _packetCapture.OnStopped -= PacketCapture_OnStopped;
                _packetCapture.Dispose();
                _packetCapture = null;
            }

            Logger.Current = CreateLogger(false);
        }

        public void Dispose()
        {
            Disconnect();
            if (_current == this)
                _current = null;
        }
    }
}
