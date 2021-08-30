using System.Threading.Tasks;

namespace VpnHood.Common.Trackers
{
    public interface ITracker
    {
        public Task<bool> TrackEvent(string category, string action, string? label = null, int? value = null);
    }
}