using EmbedIO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Threading;
using VpnHood.Common;

namespace VpnHood.Test
{
    [TestClass]
    public class Test_AppUpdater
    {
        private static string CreateAppFolder(out string appPublishInfoFile, Uri updateUri = null,
            string version = "1.0.0", Uri packageDownloadUrl = null, string packageFileName = null, string content = "old")
        {
            // Create app folder with old files
            var appDir = TestHelper.CreateNewFolder("AppUpdate-AppFolder");
            appPublishInfoFile = Path.Combine(appDir, "publish.json");
            File.WriteAllText(Path.Combine(appDir, "file1.txt"), $"file1-{content}");
            File.WriteAllText(appPublishInfoFile, JsonSerializer.Serialize(
                new PublishInfo()
                {
                    UpdateUrl = updateUri?.AbsoluteUri,
                    Version = version,
                    PackageDownloadUrl = packageDownloadUrl?.AbsoluteUri,
                    PackageFileName = packageFileName,
                    LaunchPath = $"{version}/run.dll"
                }));
            return appDir;
        }

        private static void PublishUpdateFolder(string updateFolder, string publishInfoFileName, Uri updateBaseUri = null, string version = "1.0.1")
        {
            var packageFileName = $"Package-{version}.zip";

            // create zip package offline
            var newPakageFolder = CreateAppFolder(out string appPublishInfoFile,
                version: version,
                content: "new",
                updateUri: updateBaseUri != null ? new Uri(updateBaseUri, publishInfoFileName) : null,
                packageDownloadUrl: updateBaseUri != null ? new Uri(updateBaseUri, packageFileName) : null,
                packageFileName: packageFileName
                );
            File.WriteAllText(Path.Combine(newPakageFolder, "file2.txt"), "file2-new");

            // write package
            Directory.CreateDirectory(updateFolder);
            ZipFile.CreateFromDirectory(newPakageFolder, Path.Combine(updateFolder, packageFileName));

            // write publishInfo
            File.Copy(appPublishInfoFile, Path.Combine(updateFolder, publishInfoFileName));
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
            var appFolder = CreateAppFolder(out string publishInfoFile, new Uri(webUri, remotePublishInfoFileName));
            using var appUpdater = new AppUpdater(appFolder);

            // run appUpdater
            appUpdater.Start();

            // wait for update
            var timeout = 5000;
            for (var elapsed = 0; elapsed < timeout && !appUpdater.IsUpdated; elapsed += 200)
                Thread.Sleep(200);

            // Check result
            Assert.IsTrue(appUpdater.IsUpdated, "AppUpdater should update the app after start!");
            Assert.AreEqual("1.0.1", JsonSerializer.Deserialize<PublishInfo>(File.ReadAllText(publishInfoFile)).Version);
            Assert.AreEqual("file1-new", File.ReadAllText(Path.Combine(appFolder, "file1.txt")));
            Assert.AreEqual("file2-new", File.ReadAllText(Path.Combine(appFolder, "file2.txt")));
        }

        [TestMethod]
        public void Install_update_at_start()
        {
            // Create app folder with old files
            var appFolder = CreateAppFolder(out string appPublishInfoFile);
            using var appUpdater = new AppUpdater(appFolder);

            // publish new version
            PublishUpdateFolder(appUpdater.UpdatesFolder, publishInfoFileName: Path.GetFileName(appUpdater.UpdateInfoFilePath));

            // Create app folder with old files
            appUpdater.Start();
            Assert.IsTrue(appUpdater.IsUpdated, "AppUpdater should update the app after start!");
            Assert.AreEqual(JsonSerializer.Deserialize<PublishInfo>(File.ReadAllText(appPublishInfoFile)).Version, "1.0.1");
            Assert.AreEqual("file1-new", File.ReadAllText(Path.Combine(appFolder, "file1.txt")));
            Assert.AreEqual("file2-new", File.ReadAllText(Path.Combine(appFolder, "file2.txt")));
        }

        [TestMethod]
        public void Install_update_by_fileWatcher()
        {
            // Create app folder with old files
            var appFolder = CreateAppFolder(out string appPublishInfoFile);

            // create and run appUpdater
            var isUpdated = false;
            using var appUpdater = new AppUpdater(appFolder);
            appUpdater.Updated += delegate (object sender, EventArgs e)
            {
                isUpdated = true;
            };
            appUpdater.Start();

            // publish new version
            PublishUpdateFolder(appUpdater.UpdatesFolder, publishInfoFileName: Path.GetFileName(appUpdater.UpdateInfoFilePath));

            // wait for update
            var timeout = 5000;
            for (var elapsed = 0; elapsed < timeout && !appUpdater.IsUpdated; elapsed += 200)
                Thread.Sleep(200);

            // Check result
            Assert.IsTrue(isUpdated, "Updated event should be called!");
            Assert.IsTrue(appUpdater.IsUpdated, "AppUpdater should update the app after start!");
            Assert.AreEqual(JsonSerializer.Deserialize<PublishInfo>(File.ReadAllText(appPublishInfoFile)).Version, "1.0.1");
            Assert.AreEqual("file1-new", File.ReadAllText(Path.Combine(appFolder, "file1.txt")));
            Assert.AreEqual("file2-new", File.ReadAllText(Path.Combine(appFolder, "file2.txt")));
        }
    }
}
