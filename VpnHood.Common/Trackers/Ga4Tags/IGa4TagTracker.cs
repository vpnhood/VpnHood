// ReSharper disable once CheckNamespace
namespace Ga4.Trackers.Ga4Tags;

public interface IGa4TagTracker : ITracker
{
    public Task Track(Ga4TagEvent ga4Event);
}