using System;
using System.Threading.Tasks;

namespace VpnHood.Client.Device.WinDivert
{
    public class WinDivertDevice : IDevice
    {
#pragma warning disable 0067
        public event EventHandler OnStartAsService;
#pragma warning restore 0067

        public Task<IPacketCapture> CreatePacketCapture()
        {
            var res = (IPacketCapture)new WinDivertPacketCapture();
            return Task.FromResult(res);
        }
    }
}
