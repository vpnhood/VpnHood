using System;
using System.Net;
using System.Threading.Tasks;

namespace VpnHood.Server
{
    public interface IAccessServer
    {
        Task<Access> GetAccess(ClientIdentity clientIdentity);
        Task<Access> AddUsage(AddUsageParams addUsageParams);

    }
}
