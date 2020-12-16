using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Logging;

namespace VpnHood.Common
{
    public class AppUpdater : IDisposable
    {
        public class PublishInfo
        {
            public string Version { get; set; }
            public string FileName { get; set; }
            public string UpdateUrl { get; set; }
        }

        private class LastOnlineCheck
        {
            public DateTime? DateTime { get; set; }
        }

        private FileSystemWatcher _fileSystemWatcher;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "<Pending>")]
        private ILogger _logger => VhLogger.Current;
        public Timer _timer;

        public string AppFolder { get; }
        public string UpdatesFolder { get; }
        public Uri UpdateUri { get; }
        public int CheckIntervalMinutes { get; }
        public string LauncherFilePath { get; }
        public string UpdateInfoFilePath => Path.Combine(UpdatesFolder, "publish.json");
        public string UpdatedInfoFilePath => Path.Combine(UpdatesFolder, "app_updated.json");
        public string PublishInfoPath => Path.Combine(AppFolder, "publish.json");
        private string LastCheckFilePath => Path.Combine(UpdatesFolder, "lastcheck.json");

        public event EventHandler Updated;
        public bool IsStarted => _fileSystemWatcher != null;

        public AppUpdater(AppUpdaterOptions options = null)
        {
            if (options == null) options = new AppUpdaterOptions();
            AppFolder = options.AppFolder ?? Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
            LauncherFilePath = options.LauncherFilePath ?? Path.Combine(AppFolder, "Launcher", "run.dll");
            UpdatesFolder = options.UpdatesFolder ?? Path.Combine(AppFolder, "Updates");

            UpdateUri = options.UpdateUri;
            CheckIntervalMinutes = options.CheckIntervalMinutes;
        }


        public void Start()
        {
            if (IsStarted)
                throw new InvalidOperationException("AppUpdater is already started!");

            // stop updating if app publish info does 
            if (!File.Exists(PublishInfoPath))
            {
                VhLogger.Current.LogWarning($"Could not find publish file! AppUpdater has been stopped.");
                return;
            }

            // make sure UpdatesFolder exists before start watching
            Directory.CreateDirectory(UpdatesFolder);

            _fileSystemWatcher = new FileSystemWatcher
            {
                Path = UpdatesFolder,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
                Filter = "*.json",
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _fileSystemWatcher.Changed += FileSystemWatcher_Changed;
            _fileSystemWatcher.Created += FileSystemWatcher_Changed;
            _fileSystemWatcher.Renamed += FileSystemWatcher_Changed;

            CheckUpdateOffline();

            // Create Update Interval
            if (!IsUpdated && UpdateUri != null && CheckIntervalMinutes != 0)
                _timer = new Timer((state) => CheckUpdateOnlineInterval(), null, 0, CheckIntervalMinutes * 60 * 1000);
        }

        public DateTime? LastOnlineCheckTime
        {
            get
            {
                try
                {
                    var lastCheckInfoFilePath = Path.Combine(UpdatesFolder, "LastCheckTime.txt");
                    if (File.Exists(lastCheckInfoFilePath))
                    {
                        var lastOnlineCheck = JsonSerializer.Deserialize<LastOnlineCheck>(File.ReadAllText(lastCheckInfoFilePath));
                        return lastOnlineCheck.DateTime;
                    }
                }
                catch { }
                return null;
            }
        }

        private void CheckUpdateOnlineInterval()
        {
            using var _ = _logger.BeginScope($"{VhLogger.FormatTypeName<AppUpdater>()}");

            try
            {
                // read last check
                DateTime lastOnlineCheckTime = LastOnlineCheckTime ?? DateTime.MinValue;
                if ((DateTime.Now - lastOnlineCheckTime).TotalMinutes >= CheckIntervalMinutes)
                    CheckUpdateOnline().GetAwaiter();

            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
        }

        public async Task CheckUpdateOnline()
        {
            using var _ = _logger.BeginScope($"{VhLogger.FormatTypeName<AppUpdater>()}");
            _logger.LogInformation($"Checking for update on {UpdateUri}");

            // read online version
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(10000) };
            var onlinePublishInfoJson = await httpClient.GetStringAsync(UpdateUri);
            var onlinePublishInfo = JsonSerializer.Deserialize<PublishInfo>(onlinePublishInfoJson);

            // read current version
            var curPublishInfo = JsonSerializer.Deserialize<PublishInfo>(File.ReadAllText(PublishInfoPath));

            // download if newer
            if (onlinePublishInfo.Version.CompareTo(curPublishInfo.Version) >= 0)
                await DownloadUpdate(onlinePublishInfo);

            //write lastCheckTime
            var lastCheck = new LastOnlineCheck() { DateTime = DateTime.Now };
            File.WriteAllText(LastCheckFilePath, JsonSerializer.Serialize(lastCheck));
        }

        private void CheckUpdateOffline()
        {
            using var _ = _logger.BeginScope($"{VhLogger.FormatTypeName<AppUpdater>()}");

            try
            {
                if (IsUpdateAvailableOffline)
                {
                    _logger.LogInformation($"New update available! Time: {DateTime.Now}");
                    Update();
                }

                if (IsUpdated)
                {
                    _logger.LogInformation($"New update detected! Time: {DateTime.Now}");
                    Updated?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
        }

        private async Task DownloadUpdate(PublishInfo publishInfo)
        {
            // open source stream from net
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(10000) };
            using var srcStream = await httpClient.GetStreamAsync(publishInfo.UpdateUrl);

            // create desStream
            var desFileName = $"package-{publishInfo.Version}{Path.GetExtension(publishInfo.UpdateUrl)}";
            var desFilePath = Path.Combine(UpdatesFolder, desFileName);
            using var desStream = File.Create(desFilePath);

            // download
            await srcStream.CopyToAsync(desStream);
            await desStream.DisposeAsync(); //release lock as soon as possible

            // set update file
            publishInfo.FileName = desFileName;
            File.WriteAllText(UpdateInfoFilePath, JsonSerializer.Serialize(publishInfo));
        }

        private bool IsUpdateAvailableOffline => File.Exists(UpdateInfoFilePath);

        public bool IsUpdated => File.Exists(UpdatedInfoFilePath);

        private void FileSystemWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            CheckUpdateOffline();
        }

        private static string ReadAllTextAndWait(string fileName, long retry = 5)
        {
            Exception exception = null;
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

        private void Update()
        {
            if (!IsUpdateAvailableOffline)
                throw new InvalidOperationException("There is no update available on disk!");

            // read json file
            var updateFile = "";

            try
            {
                // read current version
                var publishInfo = JsonSerializer.Deserialize<PublishInfo>(File.ReadAllText(PublishInfoPath));

                // read updateInfo and find update file
                var updateJson = ReadAllTextAndWait(UpdateInfoFilePath);
                var updateInfo = JsonSerializer.Deserialize<PublishInfo>(updateJson);
                updateFile = Path.Combine(UpdatesFolder, updateInfo.FileName);
                _logger.LogInformation($"Update File Info: File: {updateFile}\nVersion: {updateInfo.Version}");

                // install update if newer
                var curVersion = Version.Parse(publishInfo.Version);
                var version = Version.Parse(updateInfo.Version);
                if (version.CompareTo(curVersion) <= 0)
                    throw new Exception("The update file is older!");

                // unzip
                if (Path.GetExtension(updateFile).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation($"Extracting update!");
                    ZipFile.ExtractToDirectory(updateFile, AppFolder, true);

                    // change updateInfo to updatedInfo
                    if (File.Exists(UpdatedInfoFilePath)) File.Delete(UpdatedInfoFilePath);
                    File.Move(UpdateInfoFilePath, UpdatedInfoFilePath);
                }
                else
                {
                    throw new Exception("Unknown update file type!");
                }
            }
            finally
            {
                if (File.Exists(UpdateInfoFilePath)) File.Delete(UpdateInfoFilePath);
                if (File.Exists(updateFile)) File.Delete(updateFile);
            }
        }

        public void LaunchUpdated(string[] args = null)
        {
            if (!IsUpdated)
                throw new InvalidOperationException("There is no installed update!");

            File.Delete(UpdatedInfoFilePath);

            _logger.LogInformation($"\nLaunching the latest installed update!\n");
            GC.Collect();

            // create processStartInfo
            var processStartInfo = new ProcessStartInfo() { FileName = "dotnet" };
            processStartInfo.ArgumentList.Add(LauncherFilePath);
            processStartInfo.ArgumentList.Add("/delaystart");
            processStartInfo.ArgumentList.Add("/nowait");
            if (args != null)
            {
                foreach (var arg in args)
                    processStartInfo.ArgumentList.Add(arg);
            }

            Process.Start(processStartInfo);
        }

        public void Dispose()
        {
            _fileSystemWatcher?.Dispose();
            _timer?.Dispose();
        }
    }
}
