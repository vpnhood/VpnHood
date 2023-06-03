using System.Threading.Tasks;
using VpnHood.Common.JobController;

namespace VpnHood.Client.ConnectorServices;

internal class ClientStreamPool : IJob
{
    public JobSection? JobSection { get; }

    public Task RunJob()
    {
        throw new System.NotImplementedException();
    }
}