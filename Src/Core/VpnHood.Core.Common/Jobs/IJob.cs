namespace VpnHood.Core.Common.Jobs;

public interface IJob
{
    public Task RunJob();
    public JobSection JobSection { get; }
}