using System.Net;
using VpnHood.AppLib.Api.WebHost.Helpers;
using VpnHood.Test;
using WatsonWebserver.Core;
using WatsonWebserver.Lite;

namespace VpnHood.AppLib.Test.Tests;

// The settings a session takes live reach it whenever they are sent, its making included.
[TestClass]
public class ReconfigureTest : TestAppBase
{
    [TestMethod]
    public async Task A_setting_changed_while_the_app_connects_reaches_the_session()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);
        await using var app = TestAppHelper.CreateClientApp();
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());

        // the connect held at its diagnose's web check: it has read the settings, and has not handed them to
        // the service yet. The check wants a page of 100 characters at least
        var checkAsked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkEndPoint = TestWebServerLocalEps.AllocateFreeTcpEndPoint(IPAddress.Loopback);
        var webServer = new WebserverLite(
            new WebserverSettings(checkEndPoint.Address.ToString(), checkEndPoint.Port), async ctx => {
                checkAsked.TrySetResult();
                await checkRelease.Task;
                await ctx.Response.Send(new string('-', 100));
            });
        _ = webServer.StartAsync(TestCt);
        app.Diagnoser.TestHttpUris = [new Uri($"http://{checkEndPoint}/")];
        app.Diagnoser.HttpTimeout = TimeSpan.FromSeconds(30);

        var dropQuic = !app.UserSettings.DropQuic;
        try {
            var connectTask = app.Connect(vpnProfile.VpnProfileId, diagnose: true, cancellationToken: TestCt);
            await checkAsked.Task.WaitAsync(TimeSpan.FromSeconds(30), TestCt);

            // the start then writes the settings it read over this one. Dropping QUIC is the setting to watch:
            // the test server carries TCP only by the proxy, which drops it
            app.UserSettings.DropQuic = dropQuic;
            await app.ReconfigureVpnService(TestCt);
            checkRelease.TrySetResult();
            await connectTask;
        }
        finally {
            checkRelease.TrySetResult();
            webServer.TryStop();
        }

        await AssertEqualsWait(dropQuic, () => app.State.SessionStatus?.IsDropQuic, "the session's QUIC drop");
    }
}
