using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace VpnHood.Common
{
    public class AppUpdater : IDisposable
    {
        private class PublishInfo
        {
            public string Version { get; set; }
            public string LaunchPath { get; set; }
        }

        private const string FILE_LAUNCHER = "run.dll";
        private const string FILE_PUBLISH = "app_publish.txt";
        private readonly FileSystemWatcher _fileSystemWatcher = new FileSystemWatcher();
        private readonly ILogger _logger;

        public string LauncherFolder { get; }
        public string LauncherFilePath => Path.Combine(LauncherFolder, FILE_LAUNCHER);
        public string PublishFilePath => Path.Combine(LauncherFolder, FILE_PUBLISH);
        public bool IsNewPublish => File.Exists(PublishFilePath);

        public event EventHandler Published;

        public AppUpdater(ILogger logger)
        {
            _logger = logger;
            LauncherFolder = Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location));

            _fileSystemWatcher.Path = LauncherFolder;
            _fileSystemWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName;
            _fileSystemWatcher.Filter = FILE_PUBLISH;
            _fileSystemWatcher.Changed += FileSystemWatcher_Changed;
            _fileSystemWatcher.Created += FileSystemWatcher_Changed;
            _fileSystemWatcher.Renamed += FileSystemWatcher_Changed;
            _fileSystemWatcher.EnableRaisingEvents = true;
            _fileSystemWatcher.IncludeSubdirectories = false;
        }

        private void FileSystemWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (IsNewPublish)
            {
                _logger.LogInformation($"New publish detected! Time: {DateTime.Now}");
                Published?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            _fileSystemWatcher?.Dispose();
        }

        public void LaunchNewPublish(string[] args = null)
        {
            if (!IsNewPublish)
                throw new InvalidOperationException("There is no new publish");

            File.Delete(PublishFilePath);

            _logger.LogInformation($"\nLaunching the new publish!\n");
            GC.Collect();
            Thread.Sleep(2000); // wait to release

            // create processStartInfo
            ProcessStartInfo processStartInfo = new() { FileName = "dotnet" };
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
    }
}
