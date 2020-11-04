using System;
using System.Net;
using System.Threading.Tasks;

namespace VpnHood.AccessServer
{
    public interface IAccessServer
    {
        Task<Access> GetAccess(ClientIdentity clientIdentity);
        Task<Access> AddUsage(AddUsageParams addUsageParams);

    }
}
