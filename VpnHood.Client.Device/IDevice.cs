using System;
using System.Threading.Tasks;

namespace VpnHood.Client.Device
{
    public interface IDevice
    {
        event EventHandler OnStartAsService;
        Task<IPacketCapture> CreatePacketCapture();
    }

}
