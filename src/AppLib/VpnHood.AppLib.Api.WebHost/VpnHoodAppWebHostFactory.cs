using VpnHood.AppLib.Api;
using VpnHood.AppLib.WebHosting;

namespace VpnHood.AppLib.Api.WebHost;

// The head's way in: it says what its product serves, and the app decides when each host is first
// needed. Both hosts come from here so they share one unpacked web root and one loopback endpoint,
// and can never race over either.
public class VpnHoodAppWebHostFactory(WebHostOptions options) : IAppWebHostFactory
{
    private readonly Lock _lock = new();
    private WebHostShared? _shared;

    public IAppWebHost CreateLocal(WebHostCreateParams createParams)
    {
        return new VpnHoodAppWebHost(createParams, GetShared(createParams), isRemote: false);
    }

    public IAppWebHost CreateRemote(WebHostCreateParams createParams)
    {
        return new VpnHoodAppWebHost(createParams, GetShared(createParams), isRemote: true);
    }

    private WebHostShared GetShared(WebHostCreateParams createParams)
    {
        lock (_lock)
            return _shared ??= new WebHostShared(options, createParams);
    }
}
