using VpnHood.Client.App.Abstractions;
using VpnHood.Client.Device;
using VpnHood.Client.Device.Droid;

namespace VpnHood.Client.App.Droid.Common;

public class AndroidAppService : IAppService
{
    public IDevice Device { get; }  = new AndroidDevice();
}