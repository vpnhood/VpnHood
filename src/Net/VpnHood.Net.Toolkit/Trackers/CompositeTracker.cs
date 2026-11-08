using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.Net.Toolkit.Trackers;

/// <summary>
/// Provides several trackers as one: the switch and every event go to each.
/// </summary>
/// <remarks>
/// A member's failure is logged and keeps the events from no other member; a cancellation is thrown.
/// </remarks>
/// <param name="trackers">The trackers.</param>
/// <param name="isEnabled">
/// Whether the composite sends events; the members keep their own switches until <see cref="IsEnabled"/> is set.
/// </param>
public sealed class CompositeTracker(IReadOnlyList<ITracker> trackers, bool isEnabled) : ITracker
{
    /// <summary>Gets the trackers.</summary>
    public IReadOnlyList<ITracker> Trackers => trackers;

    /// <inheritdoc/>
    /// <remarks>
    /// A set goes to every member, and the composite keeps the value it is given, so whoever set it reads back what it
    /// set, whatever a member does with it.
    /// </remarks>
    public bool IsEnabled {
        get;
        set {
            field = value;
            foreach (var tracker in trackers)
                tracker.IsEnabled = value;
        }
    } = isEnabled;

    /// <inheritdoc/>
    /// <remarks>Off, it sends nothing; on, it sends the events to every member at once.</remarks>
    /// <exception cref="OperationCanceledException">The token was canceled, or a member threw a cancellation.</exception>
    public async Task Track(IEnumerable<TrackEvent> trackEvents, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
            return;

        // read once: each member reads the batch again
        var events = trackEvents.ToArray();
        await Task.WhenAll(trackers.Select(tracker => TrackMember(tracker, events, cancellationToken))).Vhc();
    }

    /// <inheritdoc/>
    /// <exception cref="OperationCanceledException">The token was canceled, or a member threw a cancellation.</exception>
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
