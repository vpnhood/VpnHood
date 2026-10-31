using VpnHood.Net.Http3;

namespace VpnHood.Test.Providers;

internal sealed class PinnedHttp3TrailerScheduleSampler(Http3TrailerSchedule schedule) : IHttp3TrailerScheduleSampler
{
    public int Samples { get; private set; }

    public Http3TrailerSchedule Sample()
    {
        Samples++;
        return schedule;
    }
}
