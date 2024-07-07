// ReSharper disable once CheckNamespace
namespace Ga4.Ga4Tracking;

public interface IGa4MeasurementTracker
{
    public bool IsEnabled { get; set; }
    public Task Track(Ga4TagEvent ga4Event, Dictionary<string, object>? userProperties = null);
}