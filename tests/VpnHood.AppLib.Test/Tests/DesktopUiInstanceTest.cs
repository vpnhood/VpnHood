using VpnHood.AppLib.Test.Daemon;
using VpnHood.AppUi.Hosting.Desktop.Commands;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.Test.Tests;

// One window per person and session: of launches at the same moment - a sign-in's start and a
// person's click - one opens the window and the others bring it forward and end.
[TestClass]
public class DesktopUiInstanceTest
{
    [TestMethod]
    public async Task Launches_at_the_same_moment_open_one_window()
    {
        const int launchCount = 8;
        var paths = new TestDesktopPaths("VpnHoodTest-" + Guid.CreateVersion7().ToString("N"));
        var ui = new TestDesktopUi();

        var claims = await Task.WhenAll(Enumerable.Range(0, launchCount)
            .Select(_ => Task.Run(() => DesktopUiInstance.TryClaim(paths, ui))));
        try {
            Assert.AreEqual(1, claims.Count(x => x != null));
            await VhTestUtil.AssertEqualsWait(launchCount - 1, () => ui.BringToFrontCount);
        }
        finally {
            foreach (var claim in claims.OfType<DesktopUiInstance>())
                await claim.DisposeAsync();
        }

        // the window gone, the next launch opens one
        await using var next = DesktopUiInstance.TryClaim(paths, ui);
        Assert.IsNotNull(next);
    }
}
