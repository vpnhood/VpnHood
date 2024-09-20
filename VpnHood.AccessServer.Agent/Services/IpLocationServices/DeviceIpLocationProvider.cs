using VpnHood.Common.IpLocations;
using VpnHood.Common.IpLocations.Providers;

namespace VpnHood.AccessServer.Agent.Services.IpLocationServices;

public class DeviceIpLocationProvider(
    ILogger<DeviceIpLocationProvider> logger)
    : CompositeIpLocationProvider(logger, CreateProviders())
{
    private static IIpLocationProvider[] CreateProviders()
    {
        // get IpLocations.bin file path from executing assembly
        var binFolder = Path.GetDirectoryName(typeof(Program).Assembly.Location);
        var localDb = Path.Combine(binFolder!, "Resources", "IpLocations.bin");
        using var localDbStream = File.OpenRead(localDb);
        var  localProvider = LocalIpLocationProvider.Deserialize(localDbStream);

        var providers = new IIpLocationProvider[] {
            localProvider
        };
        return providers;
    }
}