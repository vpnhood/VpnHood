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
        public PublishInfo PublishInfo { get; }

        public event EventHandler Updated;
        public bool IsStarted => _fileSystemWatcher != null;

        public AppUpdater(string appFolder = null, AppUpdaterOptions options = null)
        {
            if (options == null) options = new AppUpdaterOptions();
            AppFolder = appFolder ?? Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
            LauncherFilePath = options.LauncherFilePath ?? Path.Combine(AppFolder, "launcher", "run.dll");
            UpdatesFolder = options.UpdatesFolder ?? Path.Combine(AppFolder, "updates");
            UpdateUri = options.UpdateUri;
            CheckIntervalMinutes = options.CheckIntervalMinutes;

            if (File.Exists(PublishInfoPath))
            {
                PublishInfo = JsonSerializer.Deserialize<PublishInfo>(File.ReadAllText(PublishInfoPath));

                //set update Url by PublishInfo if it is not overwrited by parameter
                if (UpdateUri == null && PublishInfo.UpdateUrl != null)
                    UpdateUri = new Uri(PublishInfo.UpdateUrl);
            }
        }

        public void Start()
        {
            if (IsStarted)
                throw new InvalidOperationException("AppUpdater is already started!");

            // stop updating if app publish info does 
            if (PublishInfo == null)
            {
                VhLogger.Current.LogWarning($"Could not read publish info file. AppUpdater will not work! PublishInfo: {PublishInfoPath}");
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
            _logger.LogInformation($"Checking for update on {UpdateUri}");

            // read online version
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(10000) };
            var onlinePublishInfoJson = await httpClient.GetStringAsync(UpdateUri);
            var onlinePublishInfo = JsonSerializer.Deserialize<PublishInfo>(onlinePublishInfoJson);

            // download if newer
            if (onlinePublishInfo.Version.CompareTo(PublishInfo.Version) >= 0)
                await DownloadUpdate(onlinePublishInfo);

            //write lastCheckTime
            var lastCheck = new LastOnlineCheck() { DateTime = DateTime.Now };
            File.WriteAllText(LastCheckFilePath, JsonSerializer.Serialize(lastCheck));
        }

        private void CheckUpdateOffline()
        {
            using var _ = _logger.BeginScope($"{VhLogger.FormatTypeName<AppUpdater>()} => {nameof(CheckUpdateOffline)}");

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
            using var srcStream = await httpClient.GetStreamAsync(publishInfo.PackageDownloadUrl);

            // create desStream
            var desFilePath = Path.Combine(UpdatesFolder, publishInfo.PackageFileName);
            using var desStream = File.Create(desFilePath);

            // download
            await srcStream.CopyToAsync(desStream);
            await desStream.DisposeAsync(); //release lock as soon as possible

            // set update file
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
            var packageFile = "";

            try
            {
                // read updateInfo and find update file
                var updateJson = ReadAllTextAndWait(UpdateInfoFilePath);
                var updateInfo = JsonSerializer.Deserialize<PublishInfo>(updateJson);
                packageFile = Path.Combine(UpdatesFolder, updateInfo.PackageFileName);
                _logger.LogInformation($"Update File: {packageFile}\nVersion: {updateInfo.Version}");

                // install update if newer
                var curVersion = Version.Parse(PublishInfo.Version);
                var version = Version.Parse(updateInfo.Version);
                if (version.CompareTo(curVersion) <= 0)
                    throw new Exception($"The update file is not a newer version! CurrentVersion: {curVersion}, UpdateVersion: {version}");

                // unzip
                if (Path.GetExtension(packageFile).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation($"Extracting update!");
                    ZipFile.ExtractToDirectory(packageFile, AppFolder, true);

                    // change updateInfo to updatedInfo
                    _logger.LogInformation($"Extracting update!");
                    if (File.Exists(UpdatedInfoFilePath)) File.Delete(UpdatedInfoFilePath);
                    File.Move(UpdateInfoFilePath, UpdatedInfoFilePath); // let filewatcher run the update
                }
                else
                {
                    throw new Exception("Unknown update file type!");
                }
            }
            finally
            {
                if (File.Exists(UpdateInfoFilePath)) File.Delete(UpdateInfoFilePath);
                if (File.Exists(packageFile)) File.Delete(packageFile);
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
            if (args != null)
            {
                foreach (var arg in args)
                    processStartInfo.ArgumentList.Add(arg);
            }

            Thread.Sleep(1000); //Please wait!
            Process.Start(processStartInfo);
        }

        public void Dispose()
        {
            _fileSystemWatcher?.Dispose();
            _timer?.Dispose();
        }
    }
}
