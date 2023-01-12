using System.Threading.Tasks;

namespace VpnHood.Common.Timing;

public interface IWatchDog
{
    public Task DoWatch();
    public WatchDogChecker? WatchDogChecker { get; } 
}