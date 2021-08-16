using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using VpnHood.Common;
using VpnHood.Common.Trackers;
using VpnHood.Logging;
using VpnHood.Server.AccessServers;
using VpnHood.Server.SystemInformation;

namespace VpnHood.Server.App
{
    public class ServerApp : AppBaseNet<ServerApp>
    {
        private readonly GoogleAnalyticsTracker _googleAnalytics;
        private VpnHoodServer? _vpnHoodServer;

        public AppSettings AppSettings { get; }
        public Guid ServerId => AppSettings.ServerId ?? throw new InvalidOperationException($"{nameof(AppSettings.ServerId)} is not set!");
        public IAccessServer AccessServer { get; }
        public FileAccessServer? FileAccessServer => AccessServer as FileAccessServer;

        public ServerApp()
        {
            // load app settings
            AppSettings = LoadAppSettings(AppSettingsFilePath);

            // logger
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddNLog(NLogConfigFilePath));
            VhLogger.Instance = loggerFactory.CreateLogger("NLog");
            VhLogger.IsDiagnoseMode = AppSettings.IsDiagnoseMode;

            // tracker
            _googleAnalytics = new GoogleAnalyticsTracker(
                trackId: "UA-183010362-1",
                anonyClientId: ServerId.ToString(),
                appName: typeof(ServerApp).Assembly.GetName().Name ?? "VpnHoodServer",
                appVersion: typeof(ServerApp).Assembly.GetName().Version?.ToString() ?? "x")
            {
                IsEnabled = AppSettings.IsAnonymousTrackerEnabled
            };

            // create access server
            AccessServer = AppSettings.RestBaseUrl != null
                ? CreateRestAccessServer(baseUri: AppSettings.RestBaseUrl, authorization: AppSettings.RestAuthorization, serverId: ServerId, restCertificateThumbprint: AppSettings.RestCertificateThumbprint)
                : CreateFileccessServer(WorkingFolderPath, AppSettings.SslCertificatesPassword);
        }

        private static AppSettings LoadAppSettings(string appSettingsFilePath)
        {
            AppSettings appSettings = new();
            if (File.Exists(appSettingsFilePath))
            {
                try
                {
                    appSettings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(appSettingsFilePath))
                        ?? throw new FormatException($"AppSettings has invalid format! {appSettingsFilePath}");
                }
                catch (Exception ex)
                {
                    VhLogger.Instance.LogError($"Could not load AppSetinngs! File: {ex.Message}");
                }
            }

            if (appSettings.ServerId == null)
                appSettings.ServerId = VpnHoodServer.GetServerId();

            return appSettings;
        }

        private static FileAccessServer CreateFileccessServer(string workingFolderPath, string? sslCertificatesPassword)
        {
            var accessServerFolder = Path.Combine(workingFolderPath, "access");
            VhLogger.Instance.LogInformation($"Using FileAccessServer!, AccessFolder: {accessServerFolder}");

            var ret = new FileAccessServer(accessServerFolder, sslCertificatesPassword);
            return ret;
        }

        private static RestAccessServer CreateRestAccessServer(Uri baseUri, string? authorization, Guid serverId, string? restCertificateThumbprint)
        {
            var restAuthorization = string.IsNullOrEmpty(authorization) ? "<Notset>" : "*****";
            VhLogger.Instance.LogInformation($"Initializing ResetAccessServer!, BaseUri: {baseUri}, Authorization: {string.IsNullOrEmpty(restAuthorization)}...");

            var ret = new RestAccessServer(baseUri, authorization ?? "", serverId)
            {
                RestCertificateThumbprint = restCertificateThumbprint
            };
            return ret;
        }

        protected override void OnCommand(string[] args)
        {
            if (!Util.IsNullOrEmpty(args) && args[0] == "stop")
            {
                VhLogger.Instance.LogInformation("I have received the stop command!");
                _vpnHoodServer?.Dispose();
            }

            base.OnCommand(args);
        }

        private void StopServer(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Stop all instances of VpnHoodServer that running in this folder";
            cmdApp.OnExecute(() =>
            {
                Console.WriteLine("Sending stop server request...");
                SendCommand("stop");
            });
        }

        private void StartServer(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Run the server (default command)";
            var endpointOption = cmdApp.Option("-ep|--EndPoint", $"listening EndPoint. default is {AppSettings.EndPoint}", CommandOptionType.SingleValue);
            cmdApp.OnExecute(() =>
            {
                // find listener port
                var serverEndPoint = endpointOption.HasValue() ? IPEndPoint.Parse(endpointOption.Value()!) : AppSettings.EndPoint;
                if (IsAnotherInstanceRunning($"{typeof(ServerApp).FullName}:{serverEndPoint}:single"))
                    throw new InvalidOperationException($"Another instance is running and listening to {serverEndPoint}!");

                // check FileAccessServer
                if (FileAccessServer != null && FileAccessServer.GetAllTokenIds().Length == 0)
                {
                    VhLogger.Instance.LogWarning(
                        "There is no token in the store! Use the following command to create one:\n " +
                        "dotnet VpnHoodServer.dll gen -?");
                }


                // systemInfoProvider
                ISystemInfoProvider systemInfoProvider = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? new LinuxSystemInfoProvider() : new WinSystemInfoProvider();

                // run server
                _vpnHoodServer = new VpnHoodServer(AccessServer, new ServerOptions()
                {
                    TcpHostEndPoint = serverEndPoint,
                    Tracker = _googleAnalytics,
                    OrgStreamReadBufferSize = AppSettings.OrgStreamReadBufferSize,
                    TunnelStreamReadBufferSize = AppSettings.TunnelStreamReadBufferSize,
                    MaxDatagramChannelCount = AppSettings.MaxDatagramChannelCount,
                    SystemInfoProvider = systemInfoProvider,
                    ServerId = AppSettings.ServerId,
                });

                // track
                _googleAnalytics.TrackEvent("Usage", "ServerRun");

                // Command listener
                EnableCommandListener(true);

                // start server
                _vpnHoodServer.Start().Wait();
                while (_vpnHoodServer.State != ServerState.Disposed)
                    Thread.Sleep(1000);
                return 0;
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                NLog.LogManager.Shutdown();

            base.Dispose(disposing);
        }

        protected override void Start(string[] args)
        {
            // replace "/?"
            for (var i = 0; i < args.Length; i++)
                if (args[i] == "/?") args[i] = "-?";

            // set default
            if (args.Length == 0) args = new string[] { "start" };
            var cmdApp = new CommandLineApplication()
            {
                AllowArgumentSeparator = true,
                Name = AppName,
                FullName = "VpnHood server",
                MakeSuggestionsInErrorMessage = true,
            };

            cmdApp.HelpOption(true);
            cmdApp.VersionOption("-n|--version", AppVersion);

            cmdApp.Command("start", StartServer);
            cmdApp.Command("stop", StopServer);

            if (FileAccessServer != null)
            {
                new FileAccessServerCommand(FileAccessServer)
                    .AddCommands(cmdApp);
            }

            cmdApp.Execute(args);
        }
    }
}
