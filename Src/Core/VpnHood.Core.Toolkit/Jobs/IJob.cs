namespace VpnHood.Core.Toolkit.Jobs;

public interface IJob
{
    public Task RunJob();
    public JobSection JobSection { get; }
}