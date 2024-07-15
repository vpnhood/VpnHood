// ReSharper disable once CheckNamespace
namespace Ga4.Trackers.Ga4Measurements;

public interface IGa4MeasurementTracker : ITracker
{
    public Task Track(IEnumerable<Ga4MeasurementEvent> ga4Events);
}