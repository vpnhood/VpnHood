using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using VpnHood.Common;
using VpnHood.Common.Trackers;
using VpnHood.Common.Logging;
using VpnHood.Server.AccessServers;
using VpnHood.Server.App.SystemInformation;
using VpnHood.Server.SystemInformation;

namespace VpnHood.Server.App
{
    public class ServerApp : AppBaseNet<ServerApp>
    {
        private readonly GoogleAnalyticsTracker _googleAnalytics;
        private VpnHoodServer? _vpnHoodServer;
        public AppSettings AppSettings { get; }

        public ServerApp() : base("VpnHoodServer")
        {
            // load app settings
            AppSettings = File.Exists(AppSettingsFilePath)
                ? Util.JsonDeserialize<AppSettings>(File.ReadAllText(AppSettingsFilePath))
                : new AppSettings();

            // logger
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddNLog(NLogConfigFilePath));
            VhLogger.Instance = loggerFactory.CreateLogger("NLog");
            VhLogger.IsDiagnoseMode = AppSettings.IsDiagnoseMode;

            // tracker
            var anonyClientId = Util.GetStringMd5( GetServerId().ToString() );
            _googleAnalytics = new GoogleAnalyticsTracker(
                "UA-183010362-1",
                anonyClientId,
                typeof(ServerApp).Assembly.GetName().Name ?? "VpnHoodServer",
                typeof(ServerApp).Assembly.GetName().Version?.ToString() ?? "x")
            {
                IsEnabled = AppSettings.IsAnonymousTrackerEnabled
            };

            // create access server
            AccessServer = AppSettings.RestAccessServer != null
                ? CreateRestAccessServer(AppSettings.RestAccessServer)
                : CreateFileAccessServer(WorkingFolderPath, AppSettings.FileAccessServer);
        }

        public static Guid GetServerId()
        {
            var serverIdFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VpnHoodServer", "ServerId");

            if (File.Exists(serverIdFile) && Guid.TryParse(File.ReadAllText(serverIdFile), out var serverId))
                return serverId;

            serverId = Guid.NewGuid();
            Directory.CreateDirectory(Path.GetDirectoryName(serverIdFile)!);
            File.WriteAllText(serverIdFile, serverId.ToString());
            return serverId;
        }

        public IAccessServer AccessServer { get; }
        public FileAccessServer? FileAccessServer => AccessServer as FileAccessServer;

        private static FileAccessServer CreateFileAccessServer(string workingFolderPath, FileAccessServerOptions? options)
        {
            var accessServerFolder = Path.Combine(workingFolderPath, "access");
            VhLogger.Instance.LogInformation($"Using FileAccessServer. AccessFolder: {accessServerFolder}");
            var ret = new FileAccessServer(accessServerFolder, options ?? new FileAccessServerOptions());
            return ret;
        }

        private static RestAccessServer CreateRestAccessServer(RestAccessServerOptions options)
        {
            VhLogger.Instance.LogInformation($"Initializing ResetAccessServer. {nameof(options.BaseUrl)}: {options.BaseUrl}");
            var ret = new RestAccessServer(options);
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
            cmdApp.OnExecute(() =>
            {
                // find listener port
                var instanceName = Util.GetStringMd5(typeof(ServerApp).Assembly.Location);
                if (IsAnotherInstanceRunning($"VpnHoodServer-{instanceName}"))
                    throw new InvalidOperationException("Another instance is running!");

                // check FileAccessServer
                if (FileAccessServer != null && FileAccessServer.AccessItem_LoadAll().Length == 0)
                    VhLogger.Instance.LogWarning(
                        "There is no token in the store! Use the following command to create one:\n " +
                        "dotnet VpnHoodServer.dll gen -?");

                // systemInfoProvider
                ISystemInfoProvider systemInfoProvider = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? new LinuxSystemInfoProvider()
                    : new WinSystemInfoProvider();

                // run server
                _vpnHoodServer = new VpnHoodServer(AccessServer, new ServerOptions
                {
                    Tracker = _googleAnalytics,
                    OrgStreamReadBufferSize = AppSettings.OrgStreamReadBufferSize,
                    TunnelStreamReadBufferSize = AppSettings.TunnelStreamReadBufferSize,
                    MaxDatagramChannelCount = AppSettings.MaxDatagramChannelCount,
                    SystemInfoProvider = systemInfoProvider
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
            if (disposing && !Disposed)
                LogManager.Shutdown();

            base.Dispose(disposing);
        }

        protected override void OnStart(string[] args)
        {
            // replace "/?"
            for (var i = 0; i < args.Length; i++)
                if (args[i] == "/?")
                    args[i] = "-?";

            // set default
            if (args.Length == 0) args = new[] { "start" };
            var cmdApp = new CommandLineApplication
            {
                AllowArgumentSeparator = true,
                Name = AppName,
                FullName = "VpnHood server",
                MakeSuggestionsInErrorMessage = true
            };

            cmdApp.HelpOption(true);
            cmdApp.VersionOption("-n|--version", AppVersion);

            cmdApp.Command("start", StartServer);
            cmdApp.Command("stop", StopServer);

            if (FileAccessServer != null)
                new FileAccessServerCommand(FileAccessServer)
                    .AddCommands(cmdApp);

            cmdApp.Execute(args);
        }
    }
}