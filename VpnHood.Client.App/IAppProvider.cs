using VpnHood.Client.Device;

namespace VpnHood.Client.App;

public interface IAppProvider
{
    IDevice Device { get; }
}