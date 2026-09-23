using VpnHood.AppLib.WebHosting;

namespace VpnHood.AppLib.Api.WebHost;

// The head's way in: it names this factory in its AppOptions, and the app decides when each host is
// first needed and tells it everything - what to serve and what to serve it for - in
// WebHostCreateParams. Nothing is configured here, so a head that writes its own factory carries
// its own settings in its own type rather than in one of ours.
public class VpnHoodAppWebHostFactory : IAppWebHostFactory
{
    public IAppWebHost CreateLocal(WebHostCreateParams createParams)
    {
        return new VpnHoodAppWebHost(createParams, isRemote: false);
    }

    public IAppWebHost CreateRemote(WebHostCreateParams createParams)
    {
        return new VpnHoodAppWebHost(createParams, isRemote: true);
    }
}
