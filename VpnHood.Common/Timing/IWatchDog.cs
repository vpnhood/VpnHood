namespace VpnHood.Common.Timing;

public interface IWatchDog
{
    public void DoWatch();
    public WatchDogChecker? WatchDogChecker { get; } 
}