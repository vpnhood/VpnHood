using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common;

namespace VpnHood.App.Launcher
{
    public class Updater : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly FileSystemWatcher _fileSystemWatcher;
        private readonly ILogger _logger;

        private bool _disposed;
        private Timer? _timer;

        public Updater(string? appFolder = null, UpdaterOptions? options = null)
        {
            if (options == null) options = new UpdaterOptions();
            _logger = options.Logger;
            AppFolder = appFolder
                        ?? Path.GetDirectoryName(Path.GetDirectoryName(typeof(Updater).Assembly.Location))
                        ?? throw new ArgumentException("Could not set AppFolder", nameof(appFolder));
            CheckIntervalMinutes = options.CheckIntervalMinutes;

            var publishInfoFilePath = Path.Combine(AppFolder, "publish.json");
            Console.WriteLine(publishInfoFilePath);
            PublishInfo = JsonSerializer.Deserialize<PublishInfo>(File.ReadAllText(publishInfoFilePath))
                          ?? throw new Exception($"Could not read {nameof(PublishInfo)}!");
            UpdateUri = !string.IsNullOrEmpty(PublishInfo.UpdateUrl) ? new Uri(PublishInfo.UpdateUrl) : null;

            // make sure UpdatesFolder exists before start watching
            Directory.CreateDirectory(UpdatesFolder);

            // start system watcher
            _fileSystemWatcher = new FileSystemWatcher
            {
                Path = UpdatesFolder,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
                Filter = "*.json",
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
        }

        public string AppFolder { get; }
        public Uri? UpdateUri { get; }
        public int CheckIntervalMinutes { get; }
        public string UpdatesFolder => Path.Combine(AppFolder, "updates");
        public string NewPublishInfoFilePath => Path.Combine(UpdatesFolder, "publish.json");
        private string LastCheckFilePath => Path.Combine(UpdatesFolder, "lastcheck.json");
        public PublishInfo PublishInfo { get; }
        public CancellationToken CancelationToken => _cancellationTokenSource.Token;
        public bool IsStarted { get; private set; }

        public DateTime? LastOnlineCheckTime
        {
            get
            {
                try
                {
                    if (File.Exists(LastCheckFilePath))
                    {
                        var lastOnlineCheck = JsonSerializer.Deserialize<DateTime>(File.ReadAllText(LastCheckFilePath));
                        return lastOnlineCheck;
                    }
                }
                catch
                {
                }

                return null;
            }
        }

        private bool IsUpdateAvailableOffline => File.Exists(NewPublishInfoFilePath);

        public void Dispose()
        {
            if (!_disposed)
            {
                _fileSystemWatcher.Dispose();
                _timer?.Dispose();
                IsStarted = false;
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }

        public int Start()
        {
            if (IsStarted)
                throw new InvalidOperationException("AppLauncher is already started!");
            IsStarted = true;

            _fileSystemWatcher.Changed += FileSystemWatcher_Changed;
            _fileSystemWatcher.Created += FileSystemWatcher_Changed;
            _fileSystemWatcher.Renamed += FileSystemWatcher_Changed;

            CheckUpdateOffline();

            // Create Update Interval
            if (UpdateUri != null && CheckIntervalMinutes != 0)
                _timer = new Timer(state => CheckUpdateOnlineInterval(), null, 0, CheckIntervalMinutes * 60 * 1000);

            // launch main app
            if (!CancelationToken.IsCancellationRequested)
                return Launch();

            return -2;
        }

        private void CheckUpdateOnlineInterval()
        {
            try
            {
                // read last check
                var lastOnlineCheckTime = LastOnlineCheckTime ?? DateTime.MinValue;
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
            using var httpClient = new HttpClient {Timeout = TimeSpan.FromMilliseconds(10000)};
            var onlinePublishInfoJson = await httpClient.GetStringAsync(UpdateUri);
            var onlinePublishInfo = JsonSerializer.Deserialize<PublishInfo>(onlinePublishInfoJson) ??
                                    throw new Exception($"Could not load {nameof(PublishInfo)}!");
            _logger.LogInformation(
                $"CurrentVersion: {PublishInfo.Version}, OnlineVersion: {onlinePublishInfo.Version}");

            // check targetFramework
            var isSameTargetFramework =
                CompareTragetFramework(onlinePublishInfo.TargetFramework, PublishInfo.TargetFramework) == 0;
            if (!isSameTargetFramework)
                _logger.LogWarning(
                    $"Thre is an update that requires a new DotNet Framework. Consider full upgrade. Current TargetFramework: {PublishInfo.TargetFramework}, TargetFramework: {onlinePublishInfo.TargetFramework}");

            // download if newer
            var curVer = Version.Parse(PublishInfo.Version);
            var onlineVer = Version.Parse(onlinePublishInfo.Version);
            if (onlineVer > curVer && isSameTargetFramework)
                await DownloadUpdate(onlinePublishInfo);

            //write lastCheckTime
            File.WriteAllText(LastCheckFilePath, JsonSerializer.Serialize(DateTime.Now));
        }

        private void CheckUpdateOffline()
        {
            try
            {
                if (IsUpdateAvailableOffline)
                {
                    _logger.LogInformation("New update available!");
                    Thread.Sleep(3000);
                    ExitAndLaunchUpdater();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
        }

        private async Task DownloadUpdate(PublishInfo publishInfo)
        {
            if (string.IsNullOrEmpty(publishInfo.PackageDownloadUrl))
                throw new Exception($"Could not find : {nameof(publishInfo.PackageDownloadUrl)}");

            _logger.LogInformation($"Downloading new version! Url: {publishInfo.PackageDownloadUrl}");

            // open source stream from net
            using var httpClient = new HttpClient {Timeout = TimeSpan.FromMilliseconds(10000)};
            await using var srcStream = await httpClient.GetStreamAsync(publishInfo.PackageDownloadUrl);

            // create desStream
            if (string.IsNullOrEmpty(publishInfo.PackageFileName))
                throw new Exception(
                    $"The downloaded publishInfo does not contain {nameof(publishInfo.PackageFileName)}");
            var desFilePath = Path.Combine(UpdatesFolder, publishInfo.PackageFileName);
            await using var desStream = File.Create(desFilePath);

            // download
            await srcStream.CopyToAsync(desStream);
            await desStream.DisposeAsync(); //release lock as soon as possible

            // set update file
            File.WriteAllText(NewPublishInfoFilePath, JsonSerializer.Serialize(publishInfo));
        }

        private void FileSystemWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            CheckUpdateOffline();
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

        private static int CompareTragetFramework(string targetFramework1, string targetFramework2)
        {
            if (string.IsNullOrWhiteSpace(targetFramework1) || string.IsNullOrWhiteSpace(targetFramework2))
                return -1;

            var t1 = Version.Parse(Regex.Replace(targetFramework1, "[^0-9.]", ""));
            var t2 = Version.Parse(Regex.Replace(targetFramework2, "[^0-9.]", ""));
            ;
            var tt1 = new Version(t1.Major, t1.Minor);
            var tt2 = new Version(t2.Major, t2.Minor);
            return tt1.CompareTo(tt2);
        }

        private void ExitAndLaunchUpdater()
        {
            if (!IsUpdateAvailableOffline)
                throw new InvalidOperationException("There is no update available on disk!");

            var packageFile = "";
            try
            {
                // read updateInfo and find update file
                var newPublishInfoJson = ReadAllTextAndWait(NewPublishInfoFilePath);
                var newPublishInfo = JsonSerializer.Deserialize<PublishInfo>(newPublishInfoJson) ??
                                     throw new InvalidOperationException($"{nameof(PublishInfo)} is invalid!");
                if (string.IsNullOrEmpty(newPublishInfo.PackageFileName))
                    throw new Exception("The new publish info does not have PackageFileName! ");

                packageFile = Path.Combine(UpdatesFolder, newPublishInfo.PackageFileName);
                _logger.LogInformation($"Package File: {packageFile}\nVersion: {newPublishInfo.Version}");

                // check online version
                var curVersion = Version.Parse(PublishInfo.Version);
                var version = Version.Parse(newPublishInfo.Version);
                if (version.CompareTo(curVersion) <= 0)
                    throw new Exception(
                        $"The update file is not a newer version! CurrentVersion: {curVersion}, UpdateVersion: {version}");

                // check dotnet version
                if (CompareTragetFramework(newPublishInfo.TargetFramework, PublishInfo.TargetFramework) != 0)
                    throw new Exception(
                        $"The update requires new DotNet Framework. Consider full upgrade. Current TargetFramework: {PublishInfo.TargetFramework}, TargetFramework: {newPublishInfo.TargetFramework}");

                // copy launcher to temp folder and run with update command
                var tempLaunchFolder = Path.Combine(Path.GetTempPath(), "VpnHood.Launcher");
                var tempLauncherFilePath = Path.Combine(tempLaunchFolder, "run.dll");
                _logger.LogInformation($"Preparing updater. {tempLaunchFolder}");
                if (Directory.Exists(tempLaunchFolder)) Directory.Delete(tempLaunchFolder, true);
                var updaterSssemlyLocation = Path.GetDirectoryName(typeof(Updater).Assembly.Location) ??
                                             throw new Exception("Could not find update assembbly location!");
                DirectoryCopy(updaterSssemlyLocation, tempLaunchFolder, true);

                // dotnet tempdir/launcher.dll update package.zip appFolder orgArguments
                var args = new[] {tempLauncherFilePath, "update", packageFile, AppFolder};
                _logger.LogInformation($"Running updater: {tempLauncherFilePath}");
                Process.Start("dotnet", args.Concat(Environment.GetCommandLineArgs()));

                // cancel current process
                _cancellationTokenSource.Cancel();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error during update! Message: {ex}");

                // delete new package file if there is an error
                if (File.Exists(packageFile))
                    File.Delete(packageFile);
                throw;
            }
            finally
            {
                // delete new publish info anyway
                if (File.Exists(NewPublishInfoFilePath))
                    File.Delete(NewPublishInfoFilePath);
            }
        }

        private int Launch()
        {
            _logger.LogInformation($"\nLaunching {PublishInfo.LaunchPath}!\n");

            // create processStartInfo
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = AppFolder
            };

            // add launcher args 
            var launchPath = Path.Combine(AppFolder, PublishInfo.LaunchPath);
            processStartInfo.ArgumentList.Add(launchPath);

            // add launch arguments
            IEnumerable<string> args = Array.Empty<string>();
            if (PublishInfo.LaunchArguments != null)
                args = args.Concat(PublishInfo.LaunchArguments);

            // add original arguments
            args = args.Concat(Environment.GetCommandLineArgs()[1..]);

            // remove launcher arguments
            args = args.Where(x => x.IndexOf("-launcher:") != 0);

            // remove duplicates
            foreach (var arg in args)
                if (!processStartInfo.ArgumentList.Contains(arg))
                    processStartInfo.ArgumentList.Add(arg);

            // Start process
            var process = Process.Start(processStartInfo) ??
                          throw new Exception($"Could not start {processStartInfo.FileName}!");
            var task = process.WaitForExitAsync(CancelationToken);

            try
            {
                task.Wait(CancelationToken);
                return process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("launcher is killing server!");
                process.Kill(true);

                // must return zero otherwise let linux service will kill the updater
                return 0;
            }
        }

        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            // Get the subdirectories for the specified directory.
            var dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);

            var dirs = dir.GetDirectories();

            // If the destination directory doesn't exist, create it.       
            Directory.CreateDirectory(destDirName);

            // Get the files in the directory and copy them to the new location.
            var files = dir.GetFiles();
            foreach (var file in files)
            {
                var tempPath = Path.Combine(destDirName, file.Name);
                file.CopyTo(tempPath, false);
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
                foreach (var subdir in dirs)
                {
                    var tempPath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, tempPath, copySubDirs);
                }
        }
    }
}