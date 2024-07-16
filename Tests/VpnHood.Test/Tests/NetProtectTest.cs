using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VpnHood.Test.Tests;

[TestClass]
public class NetProtectTest : TestBase
{
    [TestMethod]
    public async Task MaxTcpWaitConnect_reject()
    {
        // create server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.TcpConnectTimeout = TimeSpan.FromSeconds(1);
        fileAccessManagerOptions.SessionOptions.MaxTcpConnectWaitCount = 0;
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token);

        try {
            await TestHelper.Test_Https();
            Assert.Fail("Exception expected!");
        }
        catch (Exception ex) {
            Assert.AreEqual(ex.GetType().Name, nameof(HttpRequestException));
        }
    }

    [TestMethod]
    public async Task MaxTcpWaitConnect_accept()
    {
        // create server
        var fileAccessManagerOptions = TestHelper.CreateFileAccessManagerOptions();
        fileAccessManagerOptions.SessionOptions.TcpConnectTimeout = TimeSpan.FromSeconds(1);
        fileAccessManagerOptions.SessionOptions.MaxTcpConnectWaitCount = 1;
        await using var server = await TestHelper.CreateServer(fileAccessManagerOptions);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = await TestHelper.CreateClient(token);

        await TestHelper.Test_Https();
        await TestHelper.Test_Https();
    }
}