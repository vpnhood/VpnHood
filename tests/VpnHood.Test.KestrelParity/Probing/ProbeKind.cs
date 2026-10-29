namespace VpnHood.Test.KestrelParity.Probing;

public enum ProbeKind
{
    // done within seconds
    Quick,

    // waits out a server's timeout, up to Kestrel's keep-alive of 130 s
    Slow,

    // megabytes of framing, run one at a time: probes beside it would slow the drain it measures
    Bulk
}
