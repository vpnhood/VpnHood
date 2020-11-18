using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace VpnHood
{
    public class AppUpdater : IDisposable
    {
        private const string PUBLISH_INFO = "publish.json";
        private readonly FileSystemWatcher _fileSystemWatcher = new FileSystemWatcher();
        private readonly ILogger _logger;

        public string PublishInfoPath { get; }
        public string NewAppPath { get; private set; }
        public event EventHandler NewVersionFound;

        public AppUpdater(ILogger logger)
        {
            var publishFolder = Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));
            PublishInfoPath = Path.Combine(publishFolder, PUBLISH_INFO);
            _logger = logger;

            _fileSystemWatcher.Path = publishFolder;
            _fileSystemWatcher.NotifyFilter = NotifyFilters.LastWrite;
            _fileSystemWatcher.Filter = PUBLISH_INFO;
            _fileSystemWatcher.Changed += FileSystemWatcher_Changed;
            _fileSystemWatcher.Created += FileSystemWatcher_Changed;
            _fileSystemWatcher.Renamed += FileSystemWatcher_Changed;
            _fileSystemWatcher.EnableRaisingEvents = true;
            _fileSystemWatcher.IncludeSubdirectories = false;
        }

        private void FileSystemWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            CheckNewerVersion();
            if (NewAppPath != null)
                NewVersionFound?.Invoke(this, EventArgs.Empty);
        }

        public bool CheckNewerVersion()
        {
            if (!File.Exists(PublishInfoPath))
                return false;

            Directory.SetCurrentDirectory(Path.GetDirectoryName(PublishInfoPath));

            // read json
            var json = ReadAllTextAndWait(PublishInfoPath);
            var publishInfo = JsonSerializer.Deserialize<PublishInfo>(json);
            var version = Version.Parse(publishInfo.Version);
            if (version.CompareTo(Assembly.GetEntryAssembly().GetName().Version) != 0)
                NewAppPath = publishInfo.LaunchPath;
            return NewAppPath != null;
        }

        private string ReadAllTextAndWait(string fileName, long retry = 5)
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

        public void Dispose()
        {
            _fileSystemWatcher?.Dispose();
        }

        public bool LaunchNewVersion(string[] args = null)
        {
            if (NewAppPath == null)
                CheckNewerVersion();

            if (NewAppPath != null)
            {
                _logger.LogInformation($"\nLaunching the new version!\n{NewAppPath}");
                GC.Collect();
                Thread.Sleep(2000); // wait to release

                // create processStartInfo
                ProcessStartInfo processStartInfo = new() { FileName = "dotnet" };
                processStartInfo.ArgumentList.Add(NewAppPath);
                if (args != null)
                {
                    foreach (var arg in args)
                        processStartInfo.ArgumentList.Add(arg);
                }

                Process.Start(processStartInfo);
                return true;
            }

            return false;
        }
    }
}
