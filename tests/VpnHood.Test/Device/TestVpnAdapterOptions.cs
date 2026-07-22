namespace VpnHood.Test.Device;

public class TestVpnAdapterOptions
{
    // Warning: enabling this adds an unscoped "udp.DstPort==53" clause to the WinDivert filter,
    // which captures all DNS on the machine and breaks the per-test IP isolation that lets
    // WinDivert tests run in parallel; scope the filter to the test's DNS servers first if a
    // test ever needs it.
    public bool SimulateDns { get; set; }
}