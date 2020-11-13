using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace VpnHood.AccessServer
{
    public class AppUpdater : IDisposable
    {
        private class PublishInfo
        {
            public string Version { get; set; }
            public string LaunchPath { get; set; }
        }

        private readonly FileSystemWatcher _fileSystemWatcher = new FileSystemWatcher();
        public string PublishJsonPath { get; }
        public string NewAppPath { get; private set; }
        public event EventHandler NewVersionFound;

        public AppUpdater()
        {
            var publishFolder = Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
            PublishJsonPath = Path.Combine(publishFolder, "publish.json");

            _fileSystemWatcher.Path = publishFolder;
            _fileSystemWatcher.NotifyFilter = NotifyFilters.LastWrite;
            _fileSystemWatcher.Filter = "publish.json";
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

        public bool CheckNewerVersion(bool changeCurrentPath = false)
        {
            if (!File.Exists(PublishJsonPath))
                return false;

            if (changeCurrentPath)
                Directory.SetCurrentDirectory(Path.GetDirectoryName(PublishJsonPath));

            // read json
            var json = ReadAllTextAndWait(PublishJsonPath);
            var publishInfo = JsonSerializer.Deserialize<PublishInfo>(json);
            var publishVersion = Version.Parse(publishInfo.Version);
            if (publishVersion != Assembly.GetExecutingAssembly().GetName().Version)
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
    }
}
