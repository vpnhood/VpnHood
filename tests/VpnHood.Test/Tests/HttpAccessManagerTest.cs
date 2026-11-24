using VpnHood.Core.Server.Access.Managers.HttpAccessManagers;
using VpnHood.Test.AccessManagers;

namespace VpnHood.Test.Tests;

[TestClass]
public class HttpAccessManagerTest : TestBase
{
    // The access manager made from its options made an HTTP client of its own, which its close left open; a caller's
    // stays the caller's
    [TestMethod]
    public void An_http_access_manager_closes_the_http_client_it_made_alone()
    {
        var options = new HttpAccessManagerOptions(new Uri("http://127.0.0.1:9/"), "Bearer");
        var accessManager = new TestOwnHttpClientAccessManager(options);
        var ownHttpClient = accessManager.Client;
        accessManager.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => ownHttpClient.CancelPendingRequests());

        using var callersHttpClient = new HttpClient();
        new HttpAccessManager(callersHttpClient, options).Dispose();
        callersHttpClient.CancelPendingRequests();
    }
}
