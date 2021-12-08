using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Logging;

namespace VpnHood.Common
{
    public abstract class AppBaseNet<T> : IDisposable where T : AppBaseNet<T>
    {
        private const string FileNamePublish = "publish.json";
        private const string FileNameSettings = "appsettings.json";
        private const string FileNameSettingsDebug = "appsettings.Debug.json";
        private const string FileNameNLogConfig = "NLog.config";
        private const string FileNameNLogXsd = "NLog.xsd";

        private static T? _instance;
        private readonly string _appCommandFilePath;
        private FileSystemWatcher? _commandListener;
        private Mutex? _instanceMutex;
        protected bool Disposed;

        protected AppBaseNet(string appName)
        {
            if (IsInit) throw new InvalidOperationException($"Only one instance of {typeof(T)} can be initialized");
            AppName = appName;

            // logger
            VhLogger.Instance = VhLogger.CreateConsoleLogger();

            // init working folder path
            WorkingFolderPath = AppFolderPath;
            var parentAppFolderPath = Path.GetDirectoryName(AppFolderPath); // check settings folder in parent folder
            if (parentAppFolderPath != null && File.Exists(Path.Combine(parentAppFolderPath, FileNamePublish)))
                WorkingFolderPath = parentAppFolderPath;
            Environment.CurrentDirectory = WorkingFolderPath;

            // init other path
            AppLocalDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName);
            AppSettingsFilePath = InitWorkingFolderFile(WorkingFolderPath,
                File.Exists(Path.Combine(WorkingFolderPath, FileNameSettingsDebug))
                    ? FileNameSettingsDebug
                    : FileNameSettings);

            // find nlog config
            NLogConfigFilePath = Path.Combine(WorkingFolderPath, FileNameNLogConfig);
            if (!File.Exists(NLogConfigFilePath))
                NLogConfigFilePath = InitWorkingFolderFile(AppFolderPath, FileNameNLogConfig);

            // init _appCommandFilePath
            _appCommandFilePath = Path.Combine(AppLocalDataPath, "appcommand.txt");

            _instance = (T)this;
        }

        public string AppName { get; }
        public string AppVersion => typeof(T).Assembly.GetName().Version?.ToString() ?? "*";

        // ReSharper disable once UnusedMember.Global
        public string ProductName =>
            ((AssemblyProductAttribute)Attribute.GetCustomAttribute(typeof(T).Assembly,
                typeof(AssemblyProductAttribute), false)).Product;

        public static T Instance =>
            _instance ?? throw new InvalidOperationException($"{typeof(T)} has not been initialized yet!");

        public static bool IsInit => _instance != null;

        public static string AppFolderPath => Path.GetDirectoryName(typeof(T).Assembly.Location) ??
                                              throw new Exception($"Could not acquire {nameof(AppFolderPath)}!");

        public string WorkingFolderPath { get; }
        public string AppSettingsFilePath { get; }
        public string NLogConfigFilePath { get; }
        public string AppLocalDataPath { get; }

        private static string OperatingSystemInfo
        {
            get
            {
                var ret = Environment.OSVersion + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit");

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

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Start(string[] args)
        {
            try
            {
                // Report current Version
                // Replace dot in version to prevent anonymity treat it as ip.
                VhLogger.Instance.LogInformation($"{typeof(T).Assembly.GetName().FullName}");
                VhLogger.Instance.LogInformation($"OS: {OperatingSystemInfo}");

                OnStart(args);
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, "Stopped program because of exception!");
                throw;
            }
        }

        public bool IsAnotherInstanceRunning(string? name = null)
        {
            name ??= typeof(T).FullName;
            _instanceMutex ??= new Mutex(false, name);

            // Make single instance
            // if you like to wait a few seconds in case that the instance is just shutting down
            return !_instanceMutex.WaitOne(TimeSpan.FromSeconds(0), false);
        }

        protected abstract void OnStart(string[] args);

        /// <summary>
        ///     Copy file from appFolder to working folder if the appFolder is different and the file exists in the appFolder
        /// </summary>
        /// <param name="workingFolder"></param>
        /// <param name="fileName"></param>
        /// <returns></returns>
        protected static string InitWorkingFolderFile(string workingFolder, string fileName)
        {
            try
            {
                var appFilePath = Path.Combine(AppFolderPath, fileName);
                var workingFolderFilePath = Path.Combine(workingFolder, fileName);
                if (appFilePath != workingFolderFilePath && !File.Exists(workingFolderFilePath) &&
                    File.Exists(appFilePath))
                {
                    VhLogger.Instance.LogInformation($"Initializing default file: {workingFolderFilePath}");
                    File.Copy(appFilePath, workingFolderFilePath);
                }

                return workingFolderFilePath;
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError($"Could not copy file, Message: {ex.Message}!");
                throw;
            }
        }

        public void EnableCommandListener(bool value)
        {
            if (!value)
            {
                _commandListener?.Dispose();
                _commandListener = null;
                return;
            }

            try
            {
                _commandListener ??= CreateCommandListener(_appCommandFilePath);
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogWarning(
                    $"Could not enable CommandListener on this machine! Message: {ex.Message}");
            }
        }

        private FileSystemWatcher CreateCommandListener(string path)
        {
            // delete old command
            if (File.Exists(path))
                File.Delete(path);

            var watchFolderPath = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(watchFolderPath);

            // watch new commands
            var commandWatcher = new FileSystemWatcher
            {
                Path = watchFolderPath,
                NotifyFilter = NotifyFilters.LastWrite,
                Filter = Path.GetFileName(path),
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            commandWatcher.Changed += (_, e) =>
            {
                var command = ReadAllTextAndWait(e.FullPath);
                OnCommand(Util.ParseArguments(command).ToArray());
            };

            return commandWatcher;
        }

        public void SendCommand(string command)
        {
            try
            {
                Console.WriteLine($"Broadcast command server. command: {command}");
                File.WriteAllText(_appCommandFilePath, command);
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError($"Could not send command! Message: {ex.Message}");
            }
        }

        private static string ReadAllTextAndWait(string fileName, long retry = 5)
        {
            Exception exception = new($"Could not read {fileName}");
            for (var i = 0; i < retry; i++)
                try
                {
                    return File.ReadAllText(fileName);
                }
                catch (IOException ex)
                {
                    exception = ex;
                    Thread.Sleep(500);
                }

            throw exception;
        }

        protected virtual void OnCommand(string[] args)
        {
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed) return;
            if (disposing)
            {
                _commandListener?.Dispose();
                _instance = null;
            }

            Disposed = true;
        }
    }
}