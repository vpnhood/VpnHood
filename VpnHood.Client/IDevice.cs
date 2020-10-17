using System;
using System.Threading.Tasks;

namespace VpnHood.Client
{
    public interface IDevice
    {
        event EventHandler OnStartAsService;
        Task<IPacketCapture> CreatePacketCapture();
    }

}
