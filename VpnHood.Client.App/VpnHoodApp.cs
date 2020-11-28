using VpnHood.Loggers;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Reflection;

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
        public bool IsDiagnoseStarted { get; private set; }
        public bool IsDisconnectedByUser { get; private set; }
        public ClientProfile ActiveClientProfile { get; private set; }
        public ClientProfile LastActiveClientProfile { get; private set; }
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
        public VpnHoodClient Client { get; private set; }
        public string AppDataFolderPath { get; }
        public string LogFilePath => Path.Combine(AppDataFolderPath, FILENAME_Log);

        public event EventHandler OnStateChanged;
        public AppSettings Settings { get; private set; }
        public AppUserSettings UserSettings => Settings.UserSettings;
        public AppFeatures Features { get; private set; }
        public ClientProfileStore ClientProfileStore { get; private set; }

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
            ConnectionState = ConnectionState,
            IsIdle = IsIdle,
            ActiveClientProfileId = ActiveClientProfile?.ClientProfileId,
            LastActiveClientProfileId = LastActiveClientProfile?.ClientProfileId,
            LastError = LastException?.Message,
            LogExists = IsIdle && File.Exists(LogFilePath),
            IsDiagnoseStarted = IsDiagnoseStarted,
            IsDisconnectedByUser = IsDisconnectedByUser
        };

        private ClientState ConnectionState
        {
            get
            {
                var state = Client?.State ?? ClientState.None;
                if ((state == ClientState.None || state == ClientState.Disposed) && _packetCapture != null)
                    state = ClientState.Disconnecting;
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

        public bool IsIdle => Client?.State == null || Client?.State == ClientState.None || Client?.State == ClientState.Disposed;

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
            LastException = null;
        }

        public Exception LastException { get; private set; }

        public async Task Connect(Guid clientProfileId, bool diagnose = false)
        {
            try
            {
                if (ActiveClientProfile != null || !IsIdle)
                    throw new InvalidOperationException("Connection is already in progress!");

                // prepare logger
                IsDiagnoseStarted = diagnose;
                LastException = null;
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
                LastActiveClientProfile = ActiveClientProfile;
                var packetCapture = await _clientAppProvider.Device.CreatePacketCapture();
                await Connect(packetCapture);

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
        }

        private async Task Connect(IPacketCapture packetCapture)
        {
            _packetCapture = packetCapture;
            packetCapture.OnStopped += PacketCapture_OnStopped;

            var token = ClientProfileStore.GetToken(ActiveClientProfile.TokenId, true);
            var OSbit = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";

            var a = _clientAppProvider.OperatingSystemInfo;
            Logger.Current.LogInformation($"OS: {_clientAppProvider.OperatingSystemInfo}, AppVersion: {typeof(VpnHoodApp).GetType().Assembly.GetName().Version.ToString().Replace('.',',')}");
            Logger.Current.LogInformation($"ClientProfileInfo: TokenId: {Logger.FormatId(token.TokenId)}, SupportId: {Logger.FormatId(token.SupportId)}, ServerEndPoint: {Logger.FormatDns(token.ServerEndPoint)}");

            // Create Client
            Client = new VpnHoodClient(
                packetCapture: packetCapture,
                clientId: Settings.ClientId,
                token: token,
                new ClientOptions()
                {
                    TcpIpChannelCount = 4,
                    IpResolveMode = IpResolveMode.Token,
                });

            Client.OnStateChanged += Client_OnStateChanged;
            await Client.Connect();
        }

        private void Client_OnStateChanged(object sender, EventArgs e)
        {
            OnStateChanged?.Invoke(this, e);
            switch (State.ConnectionState)
            {
                case ClientState.None:
                case ClientState.Disposed:
                    break;
            }
        }

        private void PacketCapture_OnStopped(object sender, EventArgs e)
        {
            Disconnect();

            var packetCapture = (IPacketCapture)sender;
            packetCapture.OnStopped -= PacketCapture_OnStopped;
            if (packetCapture == _packetCapture)
                _packetCapture = null;
        }

        public void Disconnect(bool byUser = false)
        {
            if (Client == null)
                return;
            
            ActiveClientProfile = null;
            IsDisconnectedByUser = byUser;

            Client?.Dispose();
            Client = null;
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
