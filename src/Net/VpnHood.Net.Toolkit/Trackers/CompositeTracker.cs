using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.Toolkit.Trackers;

// Several trackers as one: the switch and every event go to each. It keeps the switch it is given,
// so whoever set it reads back what it set, whatever a member does with it.
public sealed class CompositeTracker(IReadOnlyList<ITracker> trackers, bool isEnabled) : ITracker
{
    public IReadOnlyList<ITracker> Trackers => trackers;

    public bool IsEnabled {
        get;
        set {
            field = value;
            foreach (var tracker in trackers)
                tracker.IsEnabled = value;
        }
    } = isEnabled;

    public async Task Track(IEnumerable<TrackEvent> trackEvents, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
            return;

        // read once: each member reads the batch again
        var events = trackEvents.ToArray();
        await Task.WhenAll(trackers.Select(tracker => TrackMember(tracker, events, cancellationToken))).Vhc();
    }

    public Task Track(TrackEvent trackEvent, CancellationToken cancellationToken) =>
        Track([trackEvent], cancellationToken);

    // One member's failure is logged and keeps the events from no other member.
    private static async Task TrackMember(ITracker tracker, IReadOnlyList<TrackEvent> events,
        CancellationToken cancellationToken)
    {
        try {
            await tracker.Track(events, cancellationToken).Vhc();
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "A tracker could not send its events. Tracker: {Tracker}",
                tracker.GetType().Name);
        }
    }
}
