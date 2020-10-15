using System;

namespace VpnHood.Client.App
{
    public class AppDeviceReadyEventArgs : EventArgs
    {
        public AppDeviceReadyEventArgs(IDeviceInbound device)
        {
            Device = device ?? throw new ArgumentNullException(nameof(device));
        }

        public IDeviceInbound Device { get; set; }
    }
}