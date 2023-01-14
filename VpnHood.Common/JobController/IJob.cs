using System.Threading.Tasks;

namespace VpnHood.Common.JobController;

public interface IJob
{
    public Task RunJob();
    public JobSection? JobSection { get; } 
}