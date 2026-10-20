using VpnHood.Core.Tunneling.Channels;

namespace VpnHood.Test.Tests;

[TestClass]
public class ReplayWindowTest : TestBase
{
    // each seq goes in once, in any order within the window
    [TestMethod]
    public void A_seq_goes_in_once()
    {
        var window = new ReplayWindow();
        Assert.IsTrue(window.TryAdd(0));
        Assert.IsTrue(window.TryAdd(5));
        Assert.IsTrue(window.TryAdd(3));

        Assert.IsFalse(window.TryAdd(0));
        Assert.IsFalse(window.TryAdd(5));
        Assert.IsFalse(window.TryAdd(3));
        Assert.IsTrue(window.TryAdd(4));
    }

    // a seq older than the window stays out, even the first time it comes
    [TestMethod]
    public void A_seq_older_than_the_window_stays_out()
    {
        const ulong top = 1_000_000;
        var window = new ReplayWindow();
        Assert.IsTrue(window.TryAdd(top));

        Assert.IsTrue(window.TryAdd(top - ReplayWindow.Size));
        Assert.IsFalse(window.TryAdd(top - ReplayWindow.Size - 1));
    }

    // a jump of the whole ring reuses every word: none keeps a seq from before it
    [TestMethod]
    public void A_jump_clears_the_words_it_reuses()
    {
        const ulong first = 1_000_000;
        const ulong ringLater = first + 8192;
        var window = new ReplayWindow();
        for (var seq = first; seq < first + 64; seq++)
            Assert.IsTrue(window.TryAdd(seq));

        for (var seq = ringLater; seq < ringLater + 64; seq++)
            Assert.IsTrue(window.TryAdd(seq));

        Assert.IsFalse(window.TryAdd(first));
    }
}
