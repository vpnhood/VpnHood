using EmbedIO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using VpnHood.Common;

namespace VpnHood.Test
{
    [TestClass]
    public class Test_AppUpdater
    {
        private static string CreateAppFolder(out string appPublishInfoFile)
        {
            // Create app folder with old files
            var appDir = TestHelper.CreateNewFolder("AppUpdate-AppFolder");
            appPublishInfoFile = Path.Combine(appDir, "publish.json");
            File.WriteAllText(Path.Combine(appDir, "file1.txt"), "file1-old");
            File.WriteAllText(appPublishInfoFile, JsonSerializer.Serialize(new AppUpdater.PublishInfo() { Version = "1.0.0" }));
            return appDir;
        }

        private static void PublishUpdateFolder(string updateFolder, string version = "1.0.1", Uri baseUpdateUri = null)
        {
            // create zip package offline
            var newPakageFolder = TestHelper.CreateNewFolder("AppUpdate-NewPackageOffline");
            Directory.CreateDirectory(newPakageFolder);
            File.WriteAllText(Path.Combine(newPakageFolder, "file1.txt"), "file1-new");
            File.WriteAllText(Path.Combine(newPakageFolder, "file2.txt"), "file2-new");
            File.WriteAllText(Path.Combine(newPakageFolder, "publish.json"), JsonSerializer.Serialize(new AppUpdater.PublishInfo() { Version = version, FileName = "package.zip" }));

            // write package
            ZipFile.CreateFromDirectory(newPakageFolder, Path.Combine(updateFolder, "package.zip"));

            // write publishInfo
            File.WriteAllText(Path.Combine(updateFolder, "publish.json"),
                JsonSerializer.Serialize(new AppUpdater.PublishInfo()
                {
                    Version = version,
                    FileName = "package.zip",
                    UpdateUrl = baseUpdateUri != null ? new Uri(baseUpdateUri, "package.zip").AbsoluteUri : null
                }));
        }


        [TestMethod]
        public void Download_and_Install()
        {
            // Create update folder for web server
            var endPoint = TestUtil.GetFreeEndPoint();
            var webUri = new Uri($"http://localhost:{endPoint.Port}/");
            var webFolder = TestHelper.CreateNewFolder("AppUpdate-WebServer");
            PublishUpdateFolder(webFolder, baseUpdateUri: webUri);

            // Serve update older on web
            using var webServer = new WebServer(endPoint.Port);
            webServer.WithStaticFolder($"/", webFolder, false);
            webServer.Start();

            // Create app folder with old files
            var appFolder = CreateAppFolder(out string appPublishInfoFile);
            File.WriteAllText(Path.Combine(appFolder, "file1.txt"), "file1-old");
            using var appUpdater = new AppUpdater(logger: Logging.Logger.Current, new AppUpdaterOptions()
            {
                AppFolder = appFolder,
                UpdateUri = new Uri(webUri, "publish.json"),
                CheckIntervalMinutes = 100
            });

            // run appUpdater
            appUpdater.Start();

            // wait for update
            var timeout = 5000;
            for (var elapsed = 0; elapsed < timeout && !appUpdater.IsUpdated; elapsed += 200)
                Thread.Sleep(200);

            // Check result
            Assert.IsTrue(appUpdater.IsUpdated, "AppUpdater should update the app after start!");
            Assert.AreEqual("1.0.1", JsonSerializer.Deserialize<AppUpdater.PublishInfo>(File.ReadAllText(appPublishInfoFile)).Version);
            Assert.AreEqual("file1-new", File.ReadAllText(Path.Combine(appFolder, "file1.txt")));
            Assert.AreEqual("file2-new", File.ReadAllText(Path.Combine(appFolder, "file2.txt")));
        }

        [TestMethod]
        public void Install_update_at_start()
        {
            // Create app folder with old files
            var appFolder = CreateAppFolder(out string appPublishInfoFile);
            using var appUpdater = new AppUpdater(logger: Logging.Logger.Current, options: new AppUpdaterOptions { AppFolder = appFolder });

            // publish new version
            PublishUpdateFolder(appUpdater.UpdatesFolder);

            // Create app folder with old files
            appUpdater.Start();
            Assert.IsTrue(appUpdater.IsUpdated, "AppUpdater should update the app after start!");
            Assert.AreEqual(JsonSerializer.Deserialize<AppUpdater.PublishInfo>(File.ReadAllText(appPublishInfoFile)).Version, "1.0.1");
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
            using var appUpdater = new AppUpdater(logger: Logging.Logger.Current, options: new AppUpdaterOptions { AppFolder = appFolder });
            appUpdater.Updated += delegate (object sender, EventArgs e)
            {
                isUpdated = true;
            };
            appUpdater.Start();

            // publish new version
            PublishUpdateFolder(appUpdater.UpdatesFolder);

            // wait for update
            var timeout = 5000;
            for (var elapsed = 0; elapsed < timeout && !appUpdater.IsUpdated; elapsed += 200)
                Thread.Sleep(200);

            // Check result
            Assert.IsTrue(isUpdated, "Updated event should be called!");
            Assert.IsTrue(appUpdater.IsUpdated, "AppUpdater should update the app after start!");
            Assert.AreEqual(JsonSerializer.Deserialize<AppUpdater.PublishInfo>(File.ReadAllText(appPublishInfoFile)).Version, "1.0.1");
            Assert.AreEqual("file1-new", File.ReadAllText(Path.Combine(appFolder, "file1.txt")));
            Assert.AreEqual("file2-new", File.ReadAllText(Path.Combine(appFolder, "file2.txt")));
        }
    }
}
