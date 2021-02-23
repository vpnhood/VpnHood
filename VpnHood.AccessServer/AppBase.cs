using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using VpnHood.Logging;

namespace VpnHood.AccessServer
{
    public class AppBase
    {
        public delegate void RunAction(string[] args);


        private static AppBase _current;
        public static AppBase Current => _current ?? throw new InvalidOperationException($"{nameof(AppBase)} has not been initialized yet!");
        public static bool IsInit => _current != null;
        public string AppFolderPath => Path.GetDirectoryName(typeof(Program).Assembly.Location);
        public string AppSettingsFilePath => Path.Combine(WorkingFolderPath, "appsettings.json");
        public string WorkingFolderPath { get; set; }
        public string NLogConfigFilePath => Path.Combine(WorkingFolderPath, "NLog.config");

        public static void Init(RunAction run, string[] args)
        {
            _current = new AppBase();

            try
            {
                run(args);
            }
            catch (Exception exception)
            {
                VhLogger.Current.LogError(exception, "Stopped program because of exception!");
                throw;
            }
            finally
            {
                NLog.LogManager.Shutdown();
                _current = null;
            }

        }

        private AppBase()
        {
            InitWorkingFolder();

            // create logger
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddNLog(NLogConfigFilePath));
            VhLogger.Current = loggerFactory.CreateLogger("NLog");

            // Report current Version
            // Replace dot in version to prevent anonymouizer treat it as ip.
            VhLogger.Current.LogInformation($"{Assembly.GetEntryAssembly().GetName().Name}. Version: {typeof(Program).Assembly.GetName().Version.ToString().Replace('.', ',')}");
            VhLogger.Current.LogInformation($"OS: {OperatingSystemInfo}");
        }

        private void InitWorkingFolder()
        {
            WorkingFolderPath = AppFolderPath;
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
    }
}
