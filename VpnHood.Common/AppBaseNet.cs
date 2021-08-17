using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using VpnHood.Logging;

namespace VpnHood.Common
{
    public abstract class AppBaseNet<T> : IDisposable where T : AppBaseNet<T>
    {
        private const string FileName_publish = "publish.json";
        private const string FileName_Settings = "appsettings.json";
        private const string FileName_NLogConfig = "NLog.config";

        private static T? _instance;
        protected bool _disposed;
        private readonly FileSystemWatcher _commandWatcher;
        private readonly string _appCommandFilePath;
        private Mutex? _instanceMutex;
        
        public string AppName { get; }
        public string AppVersion => typeof(T).Assembly.GetName().Version?.ToString() ?? "*";
        public string ProductName => ((AssemblyProductAttribute)Attribute.GetCustomAttribute(typeof(T).Assembly, typeof(AssemblyProductAttribute), false)).Product;
        public static T Instance => _instance ?? throw new InvalidOperationException($"{typeof(T)} has not been initialized yet!");
        public static bool IsInit => _instance != null;
        public static string AppFolderPath => Path.GetDirectoryName(typeof(T).Assembly.Location) ?? throw new Exception($"Could not acquire {nameof(AppFolderPath)}!");
        public string WorkingFolderPath { get; }
        public string AppSettingsFilePath { get; }
        public string NLogConfigFilePath { get; }
        public string AppDataPath { get; }

        protected AppBaseNet(string appName)
        {
            if (IsInit) throw new InvalidOperationException($"Only one instance of {typeof(T)} can be initialized");
            AppName = appName;

            // logger
            VhLogger.Instance = VhLogger.CreateConsoleLogger();

            // init working folder path
            WorkingFolderPath = AppFolderPath;
            var parentAppFolderPath = Path.GetDirectoryName(AppFolderPath); // check settings folder in parent folder
            if (parentAppFolderPath != null && !File.Exists(Path.Combine(parentAppFolderPath, FileName_publish)))
                WorkingFolderPath = parentAppFolderPath;
            Environment.CurrentDirectory = WorkingFolderPath;

            // init other path
            AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName);
            AppSettingsFilePath = InitWorkingFolderFile(WorkingFolderPath, FileName_Settings);
            NLogConfigFilePath = InitWorkingFolderFile(WorkingFolderPath, FileName_NLogConfig);

            // initiailize command watcher
            _appCommandFilePath = Path.Combine(AppDataPath, "appcommand.txt");
            _commandWatcher = InitCommnadWatcher(_appCommandFilePath);

            _instance = (T)this;
        }

        public void Start(string[] args)
        {
            try
            {
                // Report current Version
                // Replace dot in version to prevent anonymizer treat it as ip.
                VhLogger.Instance.LogInformation($"{typeof(T).Assembly.GetName().FullName}.");
                VhLogger.Instance.LogInformation($"OS: {OperatingSystemInfo}");

                OnStart(args);
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, "Stopped program because of exception!");
                throw;
            }
            finally
            {
                _instance = null;
            }
        }

        public bool IsAnotherInstanceRunning(string? name = null)
        {
            if (name == null) name = typeof(T).FullName;
            if (_instanceMutex == null) _instanceMutex = new(false, name);

            // Make single instance
            // if you like to wait a few seconds in case that the instance is just shutting down
            return !_instanceMutex.WaitOne(TimeSpan.FromSeconds(0), false);
        }

        protected abstract void OnStart(string[] args);

        /// <summary>
        /// Copy file from appFolder to working folder if the appFolder is different and the file exists in the appFolder
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
                if (appFilePath != workingFolderFilePath && !File.Exists(workingFolderFilePath) && File.Exists(appFilePath))
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

        public void EnableCommandListener(bool value)
            => _commandWatcher.EnableRaisingEvents = value;

        private FileSystemWatcher InitCommnadWatcher(string path)
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
                EnableRaisingEvents = false
            };

            commandWatcher.Changed += (sender, e) =>
            {
                var fileName = e.FullPath;
                var command = ReadAllTextAndWait(e.FullPath);
                OnCommand(Util.ParseArguments(command).ToArray());
            };

            return commandWatcher;
        }

        public void SendCommand(string command)
        {
            Console.WriteLine($"Broadcast command server. command: {command}");
            File.WriteAllText(_appCommandFilePath, command);
        }

        private static string ReadAllTextAndWait(string fileName, long retry = 5)
        {
            Exception exception = new($"Could not read {fileName}");
            for (var i = 0; i < retry; i++)
            {
                try
                {
                    return File.ReadAllText(fileName);
                }
                catch (IOException ex)
                {
                    exception = ex;
                    Thread.Sleep(500);
                }
            }

            throw exception;
        }

        protected virtual void OnCommand(string[] args)
        {
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _commandWatcher.Dispose();
                    _instance = null;
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
