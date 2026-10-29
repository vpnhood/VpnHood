using System.Text.RegularExpressions;

namespace VpnHood.Test.KestrelParity;

// a probe whose answers differ today: why, and a pattern of what ours answers, as the reason states it, so ours
// answering otherwise on that probe, for another reason, still fails the test
internal sealed record KnownDifference(string Why, string Ours)
{
    public bool OursMatches(string ourLog)
    {
        return Regex.IsMatch(ourLog, Ours);
    }
}
