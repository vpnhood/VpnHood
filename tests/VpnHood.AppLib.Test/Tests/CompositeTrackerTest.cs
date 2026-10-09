using Ga4.Trackers;
using VpnHood.AppLib.Test.Providers;
using VpnHood.Net.Toolkit.Trackers;
using VpnHood.Test.Providers;

namespace VpnHood.AppLib.Test.Tests;

// Several trackers as one: the switch and the events reach each, and one member's failure no other.
[TestClass]
public class CompositeTrackerTest
{
    private int _batchReadCount;

    [TestMethod]
    public void It_keeps_the_switch_it_is_made_with()
    {
        // the app reads it back to tell a switch turning on, which sends the first launch
        var compositeTracker = new CompositeTracker([new TestTracker { IsEnabled = true }], isEnabled: true);
        Assert.IsTrue(compositeTracker.IsEnabled);
    }

    [TestMethod]
    public async Task The_switch_and_the_events_reach_each_member()
    {
        var first = new TestTracker { IsEnabled = true };
        var second = new TestTracker { IsEnabled = true };
        var compositeTracker = new CompositeTracker([first, second], isEnabled: true);

        // a batch is read once, whatever each member does with it
        await compositeTracker.Track(ReadBatch(), CancellationToken.None);
        Assert.AreEqual(1, _batchReadCount);
        Assert.HasCount(1, first.TrackEvents);
        Assert.HasCount(1, second.TrackEvents);

        // switched off: each member off, and nothing more sent
        compositeTracker.IsEnabled = false;
        Assert.IsFalse(compositeTracker.IsEnabled);
        Assert.IsFalse(first.IsEnabled);
        Assert.IsFalse(second.IsEnabled);
        await compositeTracker.Track(new TrackEvent { EventName = "test_off" }, CancellationToken.None);
        Assert.HasCount(1, first.TrackEvents);
        Assert.HasCount(1, second.TrackEvents);
    }

    [TestMethod]
    public async Task One_member_failing_keeps_the_events_from_no_other()
    {
        var after = new TestTracker { IsEnabled = true };
        var compositeTracker = new CompositeTracker([new ThrowingTracker(), after], isEnabled: true);

        await compositeTracker.Track(new TrackEvent { EventName = "test" }, CancellationToken.None);
        Assert.HasCount(1, after.TrackEvents);
    }

    private IEnumerable<TrackEvent> ReadBatch()
    {
        _batchReadCount++;
        yield return new TrackEvent { EventName = "test" };
    }
}
