using VpnHood.Test.Providers;

namespace VpnHood.Test.Tests;

[TestClass]
public class ClientTrackerTest : TestBase
{
    // The event of a connect that succeeded carried the server's endpoint with endpoint tracking off, the switch that
    // keeps the endpoints from the app's trackers. It carries it only with the switch on
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_connects_event_carries_the_endpoint_only_with_endpoint_tracking(bool allowEndPointTracker)
    {
        await using var server = await TestHelper.CreateServer();
        var token = await TestHelper.CreateAccessToken(server);
        var clientOptions = TestHelper.CreateClientOptions(token);
        clientOptions.AllowAnonymousTracker = true;
        clientOptions.AllowEndPointTracker = allowEndPointTracker;
        await using var client = await TestHelper.CreateClient(clientOptions);

        var tracker = client.Tracker as TestTracker ?? throw new InvalidOperationException("No test tracker.");
        var connectEvent = tracker.TrackEvents.Single(x =>
            x.EventName == "vh_connect_attempt" && Equals(x.Parameters["connected"], bool.TrueString));

        Assert.AreEqual(allowEndPointTracker, !string.IsNullOrEmpty(connectEvent.Parameters["endpoint"] as string));
    }
}
