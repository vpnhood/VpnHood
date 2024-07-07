// ReSharper disable once CheckNamespace
namespace Ga4.Ga4Tracking;

public interface IGa4TagTracker
{
    public bool IsEnabled { get; set; }
    public Task Track(IEnumerable<Ga4MeasurementEvent> ga4Events, Dictionary<string, object>? userProperties = null);
}