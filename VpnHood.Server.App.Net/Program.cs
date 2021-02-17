using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using VpnHood.Common;
using VpnHood.Common.Trackers;
using VpnHood.Logging;
using VpnHood.Server.AccessServers;
using VpnHood.Tunneling;

namespace VpnHood.Server.App
{
    class Program
    {
        public static AppSettings AppSettings { get; set; } = new AppSettings();
        public static AppData AppData { get; set; } = new AppData();
        public static bool IsFileAccessServer => AppSettings.RestBaseUrl == null;
        private static FileAccessServer _fileAccessServer;
        private static RestAccessServer _restAccessServer;
        private static VpnHoodServer _vpnHoodServer;
        private static GoogleAnalyticsTracker _googleAnalytics;
        private static AssemblyName AssemblyName => typeof(Program).Assembly.GetName();
        private static IAccessServer AccessServer => (IAccessServer)_fileAccessServer ?? _restAccessServer;
        private static string AppFolderPath => Path.GetDirectoryName(typeof(Program).Assembly.Location);
        private static string WorkingFolderPath { get; set; } = AppFolderPath;
        private static string AppSettingsFilePath => Path.Combine(WorkingFolderPath, "appsettings.json");
        private static string NLogConfigFilePath => Path.Combine(WorkingFolderPath, "NLog.config");
        private static string AppCommandFilePath => Path.Combine(WorkingFolderPath, "appCommand.txt");
        private static FileSystemWatcher _fileSystemWatcher;

        static void Main(string[] args)
        {
            // find working folder
            InitWorkingFolder();

            // create logger
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddNLog(NLogConfigFilePath));
            if (!args.Contains("stop"))
                VhLogger.Current = loggerFactory.CreateLogger("NLog");

            // Report current Version
            // Replace dot in version to prevent anonymouizer treat it as ip.
            VhLogger.Current.LogInformation($"VpnHoodServer. Version: {AssemblyName.Version.ToString().Replace('.', ',')}, Time: {DateTime.Now}");
            VhLogger.Current.LogInformation($"OS: {OperatingSystemInfo}");

            //Init AppData
            LoadAppData();

            // load AppSettings
            if (File.Exists(AppSettingsFilePath))
                AppSettings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppSettingsFilePath));

            // track run
            VhLogger.IsDiagnoseMode = AppSettings.IsDiagnoseMode;
            _googleAnalytics.IsEnabled = AppSettings.IsAnonymousTrackerEnabled;
            _googleAnalytics.TrackEvent("Usage", "ServerRun");

            // init AccessServer
            if (AppSettings.IsAnonymousTrackerEnabled)
                InitAccessServer();

            // replace "/?"
            for (var i = 0; i < args.Length; i++)
                if (args[i] == "/?") args[i] = "-?";

            // set default
            if (args.Length == 0) args = new string[] { "run" };
            var cmdApp = new CommandLineApplication()
            {
                AllowArgumentSeparator = true,
                Name = AssemblyName.Name,
                FullName = "VpnHood server",
                MakeSuggestionsInErrorMessage = true,
            };

            cmdApp.HelpOption(true);
            cmdApp.VersionOption("-n|--version", AssemblyName.Version.ToString());

            cmdApp.Command("run", RunServer);
            cmdApp.Command("stop", StopServer);

            // show file access server options
            if (IsFileAccessServer)
            {
                cmdApp.Command("print", PrintToken);
                cmdApp.Command("gen", GenerateToken);
            }

            try
            {
                cmdApp.Execute(args);
            }
            catch (ArgumentException ex)
            {
                VhLogger.Current.LogError(ex.Message);
            }
        }

        private static void StopServer(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Stop all instances of VpnHoodServer";
            cmdApp.OnExecute(() =>
            {
                Console.WriteLine("Sending stop server request...");
                File.WriteAllText(AppCommandFilePath, "stop");
            });
        }

        private static string OperatingSystemInfo
        {
            get
            {
                var ret = Environment.OSVersion.ToString() + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");

                // find linux distribution
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    if (File.Exists("/proc/version"))
                        ret += "\n" + File.ReadAllText("/proc/version");
                    else if (File.Exists("/etc/lsb-release"))
                        ret += "\n" + File.ReadAllText("/etc/lsb-release");
                }

                return ret.Trim();
            }
        }

        private static void InitWorkingFolder()
        {
            Environment.CurrentDirectory = WorkingFolderPath;
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(WorkingFolderPath), "publish.json")))
                return;

            WorkingFolderPath = Path.GetDirectoryName(WorkingFolderPath);
            Environment.CurrentDirectory = WorkingFolderPath;

            // copy nlog config if not exists
            try
            {
                if (!File.Exists(AppSettingsFilePath) && File.Exists(Path.Combine(AppFolderPath, Path.GetFileName(AppSettingsFilePath))))
                {
                    Console.WriteLine($"Initializing default app settings in {AppSettingsFilePath}");
                    File.Copy(Path.Combine(AppFolderPath, Path.GetFileName(AppSettingsFilePath)), AppSettingsFilePath);
                }
            }
            catch { }

            try
            {
                // copy app settings if not exists
                if (!File.Exists(NLogConfigFilePath) && File.Exists(Path.Combine(AppFolderPath, Path.GetFileName(NLogConfigFilePath))))
                {
                    Console.WriteLine($"Initializing default NLog config in {NLogConfigFilePath}\r\n");
                    File.Copy(Path.Combine(AppFolderPath, Path.GetFileName(NLogConfigFilePath)), NLogConfigFilePath);
                }

            }
            catch (Exception ex) { VhLogger.Current.LogInformation($"Could not copy, Message: {ex.Message}!"); }

        }

        private static void LoadAppData()
        {
            // try to load
            var appDataFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VpnHood.Server", "AppData.json");
            try
            {
                var json = File.ReadAllText(appDataFilePath);
                AppData = JsonSerializer.Deserialize<AppData>(json);
            }
            catch { }

            if (AppData == null)
                AppData = new AppData();

            // set serverId if not exists
            if (AppData.ServerId == Guid.Empty)
            {
                AppData.ServerId = Guid.NewGuid();
                var json = JsonSerializer.Serialize(AppData);
                Directory.CreateDirectory(Path.GetDirectoryName(appDataFilePath));
                File.WriteAllText(appDataFilePath, json);
            }

            _googleAnalytics = new GoogleAnalyticsTracker(trackId: "UA-183010362-1", anonyClientId: AppData.ServerId.ToString());
        }

        private static void InitAccessServer()
        {
            if (AppSettings.RestBaseUrl != null)
            {
                _restAccessServer = new RestAccessServer(AppSettings.RestBaseUrl, AppSettings.RestAuthHeader);
                var authHeader = string.IsNullOrEmpty(AppSettings.RestAuthHeader) ? "<Notset>" : "*****";
                VhLogger.Current.LogInformation($"Using ResetAccessServer!, BaseUri: {_restAccessServer.BaseUri}, AuthHeader: {authHeader}");
            }
            else
            {
                var accessServerFolder = Path.Combine(WorkingFolderPath, "access");
                _fileAccessServer = new FileAccessServer(accessServerFolder, AppSettings.SslCertificatesPassword);
                VhLogger.Current.LogInformation($"Using FileAccessServer!, AccessFolder: {accessServerFolder}");
            }
        }

        private static void GenerateToken(CommandLineApplication cmdApp)
        {
            var localIpAddress = Util.GetLocalIpAddress();
            cmdApp.Description = "Generate a token";
            var nameOption = cmdApp.Option("-name", $"TokenName. Default: <NoName>", CommandOptionType.SingleValue);
            var publicEndPointOption = cmdApp.Option("-ep", $"PublicEndPoint. Default: {localIpAddress}:443", CommandOptionType.SingleValue);
            var internalEndPointOption = cmdApp.Option("-iep", $"InternalEndPoint. Default: <null>. Leave null if your server have only one public IP", CommandOptionType.SingleValue);
            var maxClientOption = cmdApp.Option("-maxClient", $"MaximumClient. Default: 2", CommandOptionType.SingleValue);

            cmdApp.OnExecute(() =>
            {
                var accessServer = _fileAccessServer;
                var publicEndPoint = publicEndPointOption.HasValue() ? IPEndPoint.Parse(publicEndPointOption.Value()) : IPEndPoint.Parse($"{localIpAddress}:{AppSettings.Port}");
                var internalEndPoint = internalEndPointOption.HasValue() ? IPEndPoint.Parse(internalEndPointOption.Value()) : null;
                if (publicEndPoint.Port == 0) publicEndPoint.Port = AppSettings.Port; //set default port
                if (internalEndPoint != null && internalEndPoint.Port == 0) internalEndPoint.Port = AppSettings.Port; //set default port

                var accessItem = accessServer.CreateAccessItem(
                    publicEndPoint: publicEndPoint,
                    internalEndPoint: internalEndPoint,
                    maxClientCount: maxClientOption.HasValue() ? int.Parse(maxClientOption.Value()) : 2);

                Console.WriteLine($"The following token has been generated: ");
                PrintToken(accessItem.Token.TokenId);
                Console.WriteLine($"Store Token Count: {accessServer.GetAllTokenIds().Length}");
                return 0;
            });
        }

        private static void PrintToken(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Print a token";

            var tokenIdArg = cmdApp.Argument("tokenId", "tokenId (Guid) or tokenSupportId (id) to print");
            cmdApp.OnExecute(() =>
            {
                if (!Guid.TryParse(tokenIdArg.Value, out Guid tokenId))
                {
                    var supportId = int.Parse(tokenIdArg.Value);
                    try
                    {
                        tokenId = _fileAccessServer.TokenIdFromSupportId(supportId);
                    }
                    catch (KeyNotFoundException)
                    {
                        throw new KeyNotFoundException($"supportId does not exist! supportId: {supportId}");
                    }
                }

                PrintToken(tokenId);
                return 0;
            });
        }

        private static void PrintToken(Guid tokenId)
        {
            var accessItem = _fileAccessServer.AccessItem_Read(tokenId).Result;
            if (accessItem == null) throw new KeyNotFoundException($"Token does not exist! tokenId: {tokenId}");

            var access = AccessServer.GetAccess(new ClientIdentity() { TokenId = tokenId }).Result;
            if (access == null) throw new KeyNotFoundException($"Token does not exist! tokenId: {tokenId}");

            Console.WriteLine($"Token:");
            Console.WriteLine(JsonSerializer.Serialize(accessItem.Token, new JsonSerializerOptions() { WriteIndented = true }));
            Console.WriteLine($"---");
            Console.WriteLine(accessItem.Token.ToAccessKey());
            Console.WriteLine($"---");
            Console.WriteLine();
            Console.WriteLine($"Access:");
            Console.WriteLine(JsonSerializer.Serialize(access, new JsonSerializerOptions() { WriteIndented = true }));
        }

        private static void RunServer(CommandLineApplication cmdApp)
        {
            cmdApp.Description = "Run the server (default command)";
            var portOption = cmdApp.Option("-p|--port", "listening port. default is 443 (https)", CommandOptionType.SingleValue);
            cmdApp.OnExecute(() =>
            {
                var portNumber = portOption.HasValue() ? int.Parse(portOption.Value()) : AppSettings.Port;

                // check FileAccessServer
                if (_fileAccessServer != null && _fileAccessServer.GetAllTokenIds().Length == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    VhLogger.Current.LogInformation("There is no token in the store! Use the following command to create one:");
                    VhLogger.Current.LogInformation("dotnet VpnHoodServer.dll gen -?");
                    Console.ResetColor();
                }

                // run server
                _vpnHoodServer = new VpnHoodServer(AccessServer, new ServerOptions()
                {
                    TcpHostEndPoint = new IPEndPoint(IPAddress.Any, portNumber),
                    Tracker = _googleAnalytics,
                    IsDiagnoseMode = AppSettings.IsDiagnoseMode
                });

                // Command watcher
                InitCommnadWatcher(AppCommandFilePath);

                _vpnHoodServer.Start().Wait();
                while (_vpnHoodServer.State != ServerState.Disposed)
                    Thread.Sleep(1000);
                return 0;
            });
        }

        private static void InitCommnadWatcher(string path)
        {
            // delete old command
            if (File.Exists(path))
                File.Delete(path);

            // watch new commands
            _fileSystemWatcher = new FileSystemWatcher
            {
                Path = Path.GetDirectoryName(path),
                NotifyFilter = NotifyFilters.LastWrite,
                Filter = Path.GetFileName(path),
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _fileSystemWatcher.Changed += (sender, e) =>
            {
                try
                {
                    Thread.Sleep(100);
                    var cmd = File.ReadAllText(e.FullPath);
                    if (cmd == "stop")
                    {
                        VhLogger.Current.LogInformation("I have received the stop command!");
                        _vpnHoodServer?.Dispose();
                    }
                }
                catch { }
            };
        }
    }

}
