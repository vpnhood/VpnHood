using VpnHood.AppLib.Api.App;
using VpnHood.Test.Device;

namespace VpnHood.AppLib.Test.Tests;

// The settings a session takes live reach it whenever they are sent, its making included.
[TestClass]
public class ReconfigureTest : TestAppBase
{
    [TestMethod]
    public async Task A_setting_sent_before_the_session_is_up_reaches_it()
    {
        await using var server = await TestHelper.CreateServer();
        var token = TestHelper.CreateAccessToken(server);

        // the service's connect held where it makes its adapter: it has read the options, and has no client
        var adapterAsked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapterRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var device = new TestDevice(TestAppHelper, _ => {
            adapterAsked.TrySetResult();
            adapterRelease.Task.Wait(TimeSpan.FromSeconds(30));
            return new TestNullVpnAdapter();
        });
        await using var app = TestAppHelper.CreateClientApp(device: device);
        var vpnProfile = app.VpnProfileService.ImportAccessKey(token.ToAccessKey());
        var dropQuic = !app.UserSettings.DropQuic;
        try {
            var connectTask = app.Connect(vpnProfile.VpnProfileId, cancellationToken: TestCt);
            await adapterAsked.Task.WaitAsync(TimeSpan.FromSeconds(30), TestCt);

            // no error with no session to take it, and the session takes it once up. Dropping QUIC is the
            // setting to watch: the test server carries TCP only by the proxy, which drops it
            app.UserSettings.DropQuic = dropQuic;
            await app.ReconfigureVpnService(TestCt);
            adapterRelease.TrySetResult();
            await connectTask;
        }
        finally {
            adapterRelease.TrySetResult();
        }

        await app.WaitForState(AppConnectionState.Connected);
        await AssertEqualsWait(dropQuic, () => app.State.SessionStatus?.IsDropQuic, "the session's QUIC drop");
    }
}
