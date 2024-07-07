namespace Ga4.Ga4Tracking
{
    public interface ITracker
    {
        public bool IsEnabled { get; set; }
        public Task Track(TrackEvent trackEvent, Dictionary<string, object>? userProperties = null);
    }
}