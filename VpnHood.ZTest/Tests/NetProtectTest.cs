using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VpnHood.Test.Tests;

[TestClass]
public class NetProtectTest
{
    [TestMethod]
    public async Task MaxTcpWaitConnect_reject()
    {
        // create server
        var fileAccessServerOptions = TestHelper.CreateFileAccessServerOptions();
        fileAccessServerOptions.SessionOptions.TcpConnectTimeout = TimeSpan.FromSeconds(1);
        fileAccessServerOptions.SessionOptions.MaxTcpConnectWaitCount = 0;
        await using var server = TestHelper.CreateServer(fileAccessServerOptions);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = TestHelper.CreateClient(token);

        try
        {
            await TestHelper.Test_HttpsAsync();
            Assert.Fail("Exception expected!");
        }
        catch (Exception ex)
        {
            Assert.AreEqual(ex.GetType().Name, nameof(HttpRequestException));
        }
    }

    [TestMethod]
    public async Task MaxTcpWaitConnect_accept()
    {
        // create server
        var fileAccessServerOptions = TestHelper.CreateFileAccessServerOptions();
        fileAccessServerOptions.SessionOptions.TcpConnectTimeout = TimeSpan.FromSeconds(1);
        fileAccessServerOptions.SessionOptions.MaxTcpConnectWaitCount = 1;
        await using var server = TestHelper.CreateServer(fileAccessServerOptions);

        // create client
        var token = TestHelper.CreateAccessToken(server);
        await using var client = TestHelper.CreateClient(token);

        await TestHelper.Test_HttpsAsync();
        await TestHelper.Test_HttpsAsync();
    }

}