using System.Text.RegularExpressions;

namespace VpnHood.Test.KestrelParity.Probing;

// what each server's answer to one probe looked like; Failed when the probe threw on either, so measured nothing
internal sealed partial record ProbeResult(string Name, ProbeKind Kind, string Reference, string Ours, bool Failed)
{
    // A timing within a second counts as the same: Kestrel's timeouts fire on a one-second heartbeat, so a wait of
    // 5 s or more, on either side, moves by that much between its own runs, and a bulk probe's times are how fast
    // each side drains megabytes on this machine, which moves as much. How long a socket stayed open is the probe's
    // own observation time, not the server's, and it varies with scheduling when hundreds of probes run.
    public bool IsSame
    {
        get {
            if (Reference == Ours)
                return true;

            var reference = OpenAfterRegex().Replace(Reference, "= open");
            var ours = OpenAfterRegex().Replace(Ours, "= open");
            var referenceTimes = AfterRegex().Matches(reference).Select(x => int.Parse(x.Groups[1].Value)).ToArray();
            var ourTimes = AfterRegex().Matches(ours).Select(x => int.Parse(x.Groups[1].Value)).ToArray();
            return referenceTimes.Length == ourTimes.Length &&
                   AfterRegex().Replace(reference, "after #s") == AfterRegex().Replace(ours, "after #s") &&
                   referenceTimes.Zip(ourTimes).All(x => x.First == x.Second ||
                                                         ((Math.Max(x.First, x.Second) >= 5 || Kind == ProbeKind.Bulk) &&
                                                          Math.Abs(x.First - x.Second) <= 1));
        }
    }

    [GeneratedRegex(@"= open after \d+s")]
    private static partial Regex OpenAfterRegex();

    [GeneratedRegex(@"after (\d+)s")]
    private static partial Regex AfterRegex();
}
