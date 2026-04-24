using System.Text.Json;
using VpnHood.Core.Server;
using VpnHood.Core.Server.Access.Configurations;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Test.AccessManagers;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests.Servers;

[TestClass]
public class ConfigErrorTrackerTest : TestBase
{
    private static ConfigErrorTracker CreateTracker(string storagePath,
        TimeSpan? strikeDuration = null,
        TimeSpan? retryInterval = null) =>
        new(storagePath,
            strikeDuration: strikeDuration ?? TimeSpan.FromDays(7),
            retryInterval: retryInterval ?? TimeSpan.FromHours(2));

    private string CreateTempStorage()
    {
        var path = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteStrikeFile(string storagePath, DateTime firstErrorTime)
    {
        var filePath = Path.Combine(storagePath, ConfigErrorTracker.StrikeFileName);
        var json = JsonSerializer.Serialize(new {
            FirstErrorTime = firstErrorTime,
            LastErrorTime = DateTime.UtcNow,
            LastErrorMessage = "test error"
        });
        File.WriteAllText(filePath, json);
    }

    private VpnHoodServer CreateServer(TestAccessManager accessManager,
        TimeSpan? configureInterval = null,
        TimeSpan? strikeDuration = null,
        TimeSpan? retryInterval = null)
    {
        return new VpnHoodServer(accessManager, new ServerOptions {
            SocketFactory = new TestSocketFactory(),
            StoragePath = TestHelper.WorkingPath,
            AutoDisposeAccessManager = false,
            PublicIpDiscovery = false,
            ConfigureInterval = configureInterval ?? TimeSpan.FromMilliseconds(200),
            ConfigErrorStrikeDuration = strikeDuration ?? TimeSpan.FromDays(7),
            ConfigErrorRetryInterval = retryInterval ?? TimeSpan.FromHours(2)
        });
    }

    // -------------------------------------------------------------------
    // Unit tests
    // -------------------------------------------------------------------

    [TestMethod]
    public void IsPaused_returns_false_when_no_error()
    {
        var storagePath = CreateTempStorage();
        try {
            var tracker = CreateTracker(storagePath);
            Assert.IsFalse(tracker.IsPaused, "IsPaused should be false when no error has been recorded.");
        }
        finally {
            Directory.Delete(storagePath, true);
        }
    }

    [TestMethod]
    public void IsPaused_returns_false_before_strike_duration()
    {
        var storagePath = CreateTempStorage();
        try {
            var tracker = CreateTracker(storagePath);
            tracker.RecordError(new Exception("test error"));
            Assert.IsFalse(tracker.IsPaused, "IsPaused should be false when strike duration has not been reached.");
        }
        finally {
            Directory.Delete(storagePath, true);
        }
    }

    [TestMethod]
    public void IsPaused_returns_false_on_first_call_even_when_expired()
    {
        var storagePath = CreateTempStorage();
        try {
            WriteStrikeFile(storagePath, DateTime.UtcNow.AddDays(-30));
            var tracker = CreateTracker(storagePath);

            Assert.IsFalse(tracker.IsPaused,
                "IsPaused should return false on the first call to allow at least one attempt.");

            Assert.IsTrue(tracker.IsPaused,
                "IsPaused should return true on the second call when paused and retry interval not elapsed.");
        }
        finally {
            Directory.Delete(storagePath, true);
        }
    }

    [TestMethod]
    public void IsPaused_allows_retry_after_interval()
    {
        var storagePath = CreateTempStorage();
        try {
            WriteStrikeFile(storagePath, DateTime.UtcNow.AddDays(-30));
            var tracker = CreateTracker(storagePath, retryInterval: TimeSpan.FromMilliseconds(100));

            Assert.IsFalse(tracker.IsPaused);
            Assert.IsTrue(tracker.IsPaused);

            Thread.Sleep(150);
            Assert.IsFalse(tracker.IsPaused, "IsPaused should return false after RetryInterval has elapsed.");
        }
        finally {
            Directory.Delete(storagePath, true);
        }
    }

    [TestMethod]
    public void ReportSuccess_clears_pause()
    {
        var storagePath = CreateTempStorage();
        try {
            var tracker = CreateTracker(storagePath, strikeDuration: TimeSpan.FromMilliseconds(1));
            tracker.RecordError(new Exception("test error"));
            Thread.Sleep(10);
            tracker.RecordError(new Exception("test error again"));

            Assert.IsFalse(tracker.IsPaused);
            Assert.IsTrue(tracker.IsPaused);

            tracker.ReportSuccess();

            Assert.IsFalse(tracker.IsPaused, "IsPaused should be false after ReportSuccess.");
            Assert.IsFalse(File.Exists(Path.Combine(storagePath, ConfigErrorTracker.StrikeFileName)),
                "Strike file should be deleted after ReportSuccess.");
        }
        finally {
            Directory.Delete(storagePath, true);
        }
    }

    [TestMethod]
    public void Strike_persists_across_instances()
    {
        var storagePath = CreateTempStorage();
        try {
            var tracker1 = CreateTracker(storagePath);
            tracker1.RecordError(new Exception("persistent error"));

            var tracker2 = CreateTracker(storagePath);

            Assert.IsFalse(tracker2.IsPaused,
                "New instance should not be paused when strike duration not reached.");
            Assert.IsTrue(File.Exists(Path.Combine(storagePath, ConfigErrorTracker.StrikeFileName)),
                "Strike file should persist across instances.");
        }
        finally {
            Directory.Delete(storagePath, true);
        }
    }

    [TestMethod]
    public void Unreadable_file_causes_pause()
    {
        var storagePath = CreateTempStorage();
        try {
            File.WriteAllText(Path.Combine(storagePath, ConfigErrorTracker.StrikeFileName), "NOT VALID JSON{{{");
            var tracker = CreateTracker(storagePath);

            Assert.IsFalse(tracker.IsPaused,
                "First call should return false even with corrupt file.");

            Assert.IsTrue(tracker.IsPaused,
                "IsPaused should return true when strike file is corrupt.");
        }
        finally {
            Directory.Delete(storagePath, true);
        }
    }

    // -------------------------------------------------------------------
    // Integration tests
    // -------------------------------------------------------------------

    [TestMethod]
    public async Task Server_should_stay_Waiting_when_configure_fails_before_strike_duration()
    {
        // Arrange: access manager that always throws on configure
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.ServerConfigureException = new Exception("bad config");

        // Create server with short intervals but long strike duration — should keep retrying
        await using var server = CreateServer(accessManager);

        // Act
        await server.Start(TestCt);

        // Assert: server should be in Waiting state (configure failed, but strike not expired)
        Assert.AreEqual(ServerState.Waiting, server.State,
            "Server should remain in Waiting state when configure fails but strike duration is not reached.");

        // Verify strike file was created
        Assert.IsTrue(File.Exists(Path.Combine(TestHelper.WorkingPath, ConfigErrorTracker.StrikeFileName)),
            "Strike file should be created after configuration failure.");
    }

    [TestMethod]
    public async Task Server_should_pause_retries_after_strike_duration_exceeded()
    {
        // Arrange: write an old strike file (simulating failures over a long period)
        Directory.CreateDirectory(TestHelper.WorkingPath);
        WriteStrikeFile(TestHelper.WorkingPath, DateTime.UtcNow.AddDays(-30));

        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.ServerConfigureException = new Exception("persistent bad config");

        await using var server = CreateServer(accessManager,
            configureInterval: TimeSpan.FromMilliseconds(100),
            retryInterval: TimeSpan.FromMilliseconds(100));

        // Act: start the server — first call to IsPaused returns false (first attempt allowed)
        await server.Start(TestCt);

        // Server tried once and failed — state should be Waiting but tracker is paused
        Assert.AreEqual(ServerState.Waiting, server.State);

        // Act: trigger another ConfigureAndSendStatus — this time IsPaused should block it
        var lastConfigureTime = accessManager.LastConfigureTime;
        await server.ConfigureAndSendStatus(TestCt);

        // Assert: configure should NOT have been called again (IsPaused blocked it)
        Assert.AreEqual(lastConfigureTime, accessManager.LastConfigureTime,
            "Server should not retry configure when IsPaused is true.");
    }

    [TestMethod]
    public async Task Server_should_recover_when_configure_succeeds_after_errors()
    {
        // Arrange: start with a failing access manager
        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.ServerConfigureException = new Exception("temporary failure");

        await using var server = CreateServer(accessManager);

        // Act: start fails
        await server.Start(TestCt);
        Assert.AreEqual(ServerState.Waiting, server.State);
        Assert.IsTrue(File.Exists(Path.Combine(TestHelper.WorkingPath, ConfigErrorTracker.StrikeFileName)),
            "Strike file should exist after failure.");

        // Fix the access manager
        accessManager.ServerConfigureException = null;

        // Retry configure
        await server.ConfigureAndSendStatus(TestCt);

        // Assert: server should be Ready and strike file should be cleared
        Assert.AreEqual(ServerState.Ready, server.State,
            "Server should be Ready after successful configuration.");
        Assert.IsFalse(File.Exists(Path.Combine(TestHelper.WorkingPath, ConfigErrorTracker.StrikeFileName)),
            "Strike file should be deleted after successful configuration.");
    }

    [TestMethod]
    public async Task Server_should_allow_one_retry_per_interval_when_paused()
    {
        // Arrange: write an old strike file
        Directory.CreateDirectory(TestHelper.WorkingPath);
        WriteStrikeFile(TestHelper.WorkingPath, DateTime.UtcNow.AddDays(-30));

        using var accessManager = TestHelper.CreateAccessManager();
        accessManager.ServerConfigureException = new Exception("still broken");

        await using var server = CreateServer(accessManager,
            configureInterval: TimeSpan.FromMilliseconds(50),
            retryInterval: TimeSpan.FromMilliseconds(200));

        // Act: start (first attempt is always allowed)
        await server.Start(TestCt);
        var configureCount1 = accessManager.LastConfigureTime;

        // Immediate retry should be blocked
        await server.ConfigureAndSendStatus(TestCt);
        Assert.AreEqual(configureCount1, accessManager.LastConfigureTime,
            "Immediate retry should be blocked by IsPaused.");

        // Wait for retry interval to elapse
        await Task.Delay(250, TestCt);

        // Now retry should be allowed
        await server.ConfigureAndSendStatus(TestCt);
        Assert.AreNotEqual(configureCount1, accessManager.LastConfigureTime,
            "Retry should be allowed after RetryInterval has elapsed.");
    }
}
