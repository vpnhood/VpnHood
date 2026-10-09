using Ga4.Trackers;

namespace VpnHood.AppLib.Test.Providers;

// A tracker that fails before it even returns a task, the hardest case for whoever calls it.
public class ThrowingTracker : ITracker
{
    public bool IsEnabled { get; set; } = true;

    public Task Track(IEnumerable<TrackEvent> trackEvents, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("This tracker always fails.");
    }

    public Task Track(TrackEvent trackEvent, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("This tracker always fails.");
    }
}
