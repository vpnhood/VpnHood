using VpnHood.Client.Device;

namespace VpnHood.Client.App.Abstractions;

public interface IAppService
{
    IDevice Device { get; }
}