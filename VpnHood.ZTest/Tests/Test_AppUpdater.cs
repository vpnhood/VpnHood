using EmbedIO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VpnHood.Common;

namespace VpnHood.Test
{
    [TestClass]
    public class Test_AppUpdater
    {
        private class AppFolder
        {
            public string Folder { get; set; }
            public string LauncherFolder { get; set; }
            public string LauncherFile { get; set; }
            public string PublishInfoFile { get; set; }
            public PublishInfo PublishInfo { get; set; }
            public string UpdatesFolder { get; set; }
            public string SessionName { get; } = $"VhUpdaterTest-{Guid.NewGuid()}";
            private Process? _process;

            public AppFolder(Uri? updateUri = null, string version = "1.0.0", Uri? packageDownloadUrl = null, string? packageFileName = null, string content = "old", string? targetFramework = null)
            {
                var folder = TestHelper.CreateNewFolder("AppUpdate-AppFolder");
                Folder = folder;
                PublishInfoFile = Path.Combine(folder, "publish.json");
                LauncherFolder = Path.Combine(folder, "launcher");
                LauncherFile = Path.Combine(folder, "launcher", "run.dll");
                UpdatesFolder = Path.Combine(folder, "updates");
                PublishInfo = new PublishInfo(version: version, targetFramework: targetFramework ?? $"net{Environment.Version}", launchPath: $"launcher/run.dll")
                {
                    UpdateUrl = updateUri?.AbsoluteUri,
                    PackageDownloadUrl = packageDownloadUrl?.AbsoluteUri,
                    PackageFileName = packageFileName,
                    LaunchArguments = new string[] { "test", $"-sessionName:{SessionName}"},
                };

                File.WriteAllText(Path.Combine(Folder, "file1.txt"), $"file1-{content}");
                File.WriteAllText(PublishInfoFile, JsonSerializer.Serialize(PublishInfo));

                // copy launcher bin folder
                var orgLauncherFolder = Path.GetDirectoryName(typeof(Test_AppUpdater).Assembly.Location)?.Replace("VpnHood.ZTest", "VpnHood.App.Launcher") 
                    ?? throw new Exception($"Could not get orgLauncherFolder");
                Util.DirectoryCopy(orgLauncherFolder, LauncherFolder, true);
            }

            public Process Launch()
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    CreateNoWindow = true
                };
                processStartInfo.ArgumentList.Add(LauncherFile);
                processStartInfo.ArgumentList.Add($"-launcher:noLaunchAfterUpdate");
                processStartInfo.ArgumentList.Add($"-launcher:sessionName:{SessionName}");
                _process = Process.Start(processStartInfo) ?? throw new Exception("Could not start process!");
                return _process;
            }

            public bool WaitForFinish(int timeout = 5000)
            {
                if (_process == null) throw new Exception("Process has not been launched!");
                if (!_process.WaitForExit(timeout))
                    return false;

                // wait for updater to create mutex
                Thread.Sleep(1000);

                var wait = 200;
                for (var elapsed = 0; elapsed < timeout; elapsed += wait)
                {
                    if (!Mutex.TryOpenExisting(SessionName, out var mutex))
                        return true;
                    mutex.Dispose();
                    Thread.Sleep(wait);
                }

                return false;
            }
        }

        private static void PublishUpdateFolder(string updateFolder, string publishInfoFileName, Uri? updateBaseUri = null, string version = "1.0.1")
        {
            var packageFileName = $"Package-{version}.zip";

            // create zip package offline
            var appFolder = new AppFolder(
                version: version,
                content: "new",
                updateUri: updateBaseUri != null ? new Uri(updateBaseUri, publishInfoFileName) : null,
                packageDownloadUrl: updateBaseUri != null ? new Uri(updateBaseUri, packageFileName) : null,
                packageFileName: packageFileName
                );
            File.WriteAllText(Path.Combine(appFolder.Folder, "file2.txt"), "file2-new");

            // write package
            Directory.CreateDirectory(updateFolder);
            ZipFile.CreateFromDirectory(appFolder.Folder, Path.Combine(updateFolder, packageFileName));

            // write publishInfo
            File.Copy(appFolder.PublishInfoFile, Path.Combine(updateFolder, publishInfoFileName));
        }

        private static bool WaitForContent(string filePath, string content, int timeout = 5000)
        {
            for (var elapsed = 0; elapsed < timeout; elapsed += 200)
            {
                try
                {
                    if (File.ReadAllText(filePath) == content)
                        return true;

                }
                catch (Exception) { }
                Thread.Sleep(200);
            }

            return false;
        }

        [TestMethod]
        public void Download_and_Install()
        {
            // Create update folder for web server
            var endPoint = Util.GetFreeEndPoint(IPAddress.Loopback);
            var webUri = new Uri($"http://{endPoint}/");
            var remotePublishInfoFileName = "OnlinePublish.json";
            var webFolder = TestHelper.CreateNewFolder("AppUpdate-WebServer");
            PublishUpdateFolder(webFolder, remotePublishInfoFileName, updateBaseUri: webUri);

            // Serve update older on web
            using var webServer = new WebServer(endPoint.Port);
            webServer.WithStaticFolder($"/", webFolder, false);
            webServer.Start();

            // Create app folder with old files
            var appFolder = new AppFolder(new Uri(webUri, remotePublishInfoFileName));
            appFolder.Launch();
            if (!appFolder.WaitForFinish())
                Assert.Fail("Launcher has not been exited!");

            // Check result
            Assert.AreEqual("1.0.1", JsonSerializer.Deserialize<PublishInfo>(File.ReadAllText(appFolder.PublishInfoFile))?.Version);
            Assert.AreEqual("file1-new", File.ReadAllText(Path.Combine(appFolder.Folder, "file1.txt")));
            Assert.AreEqual("file2-new", File.ReadAllText(Path.Combine(appFolder.Folder, "file2.txt")));
        }

        [TestMethod]
        public void Install_update_at_start()
        {
            // Create app folder with old files
            var appFolder = new AppFolder();

            // publish new version
            PublishUpdateFolder(appFolder.UpdatesFolder, Path.Combine(appFolder.UpdatesFolder, "publish.json"));

            // wait for app to exit
            appFolder.Launch();
            if (!appFolder.WaitForFinish())
                Assert.Fail("Launcher has not been exited!");

            // Check result
            Assert.AreEqual("1.0.1", JsonSerializer.Deserialize<PublishInfo>(File.ReadAllText(appFolder.PublishInfoFile))!.Version);
            Assert.AreEqual("file1-new", File.ReadAllText(Path.Combine(appFolder.Folder, "file1.txt")));
            Assert.AreEqual("file2-new", File.ReadAllText(Path.Combine(appFolder.Folder, "file2.txt")));
        }

        [TestMethod]
        public void Install_update_at_start_failed_due_to_TargetFramework()
        {
            // Create app folder with old files
            var appFolder = new AppFolder(targetFramework: "dotnet2.1");

            // publish new version
            PublishUpdateFolder(appFolder.UpdatesFolder, Path.Combine(appFolder.UpdatesFolder, "publish.json"));

            // wait for app to exit
            appFolder.Launch();
            appFolder.WaitForFinish();

            // Check result
            Assert.AreEqual("1.0.0", JsonSerializer.Deserialize<PublishInfo>(File.ReadAllText(appFolder.PublishInfoFile))?.Version);
            Assert.AreEqual("file1-old", File.ReadAllText(Path.Combine(appFolder.Folder, "file1.txt")));
            Assert.IsFalse(File.Exists("file2.txt"));
        }

        [TestMethod]
        public void Install_update_by_fileWatcher()
        {
            // Create app folder with old files
            var appFolder = new AppFolder();
            appFolder.Launch();

            // publish new version
            PublishUpdateFolder(appFolder.UpdatesFolder, Path.Combine(appFolder.UpdatesFolder, "publish.json"));

            // wait for app to exit
            if (!appFolder.WaitForFinish())
                Assert.Fail("Launcher has not been exited!");

            // wait for updater in the other process to finish its job
            WaitForContent(Path.Combine(appFolder.Folder, "file1.txt"), "file1-new");

            // Check result
            Assert.AreEqual(JsonSerializer.Deserialize<PublishInfo>(File.ReadAllText(appFolder.PublishInfoFile))?.Version, "1.0.1");
            Assert.AreEqual("file1-new", File.ReadAllText(Path.Combine(appFolder.Folder, "file1.txt")));
            Assert.AreEqual("file2-new", File.ReadAllText(Path.Combine(appFolder.Folder, "file2.txt")));
        }
    }
}
