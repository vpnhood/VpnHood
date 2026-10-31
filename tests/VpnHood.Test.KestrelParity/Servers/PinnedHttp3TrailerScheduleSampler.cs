using VpnHood.Net.Http3;

namespace VpnHood.Test.KestrelParity.Servers;

internal sealed class PinnedHttp3TrailerScheduleSampler(Http3TrailerSchedule schedule) : IHttp3TrailerScheduleSampler
{
    public Http3TrailerSchedule Sample() => schedule;
}
