using VpnHood.Core.Common;

namespace VpnHood.Test.Tests;

[TestClass]
public class CommandListenerTest : TestBase
{
    // The close stopped the watch but left the listener saying it was still started
    [TestMethod]
    public void A_closed_listener_is_stopped()
    {
        var listener = new CommandListener(Path.Combine(TestHelper.WorkingPath, "command.txt"));
        listener.Start();
        Assert.IsTrue(listener.IsStarted);

        listener.Dispose();
        Assert.IsFalse(listener.IsStarted);
    }
}
