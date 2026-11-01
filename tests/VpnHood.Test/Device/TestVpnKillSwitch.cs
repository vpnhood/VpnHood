using VpnHood.Core.Client.VpnServices.Abstractions;

namespace VpnHood.Test.Device;

// A kill switch's block in memory, where a test plays a desktop: the rules the OS's firewall would hold.
public class TestVpnKillSwitch : IVpnKillSwitch
{
    private readonly Lock _lock = new();

    // this app's block, while it is up
    public VpnKillSwitchRules? Rules { get; private set; }

    // another app's block, which only the tunnel's holder removes (Unblock's anyApp)
    public bool IsOtherAppBlocking { get; set; }
    public bool IsUp => Rules != null || IsOtherAppBlocking;
    public int BlockCount { get; private set; }

    // a block that cannot go up, as an OS's refusal
    public Exception? BlockException { get; set; }

    public Task<bool> IsBlocking(CancellationToken cancellationToken)
    {
        return Task.FromResult(Rules != null);
    }

    public Task Block(VpnKillSwitchRules rules, CancellationToken cancellationToken)
    {
        lock (_lock) {
            if (BlockException != null)
                throw BlockException;

            Rules = rules;
            IsOtherAppBlocking = false;
            BlockCount++;
        }

        return Task.CompletedTask;
    }

    public Task Unblock(bool anyApp, CancellationToken cancellationToken)
    {
        lock (_lock) {
            Rules = null;
            if (anyApp)
                IsOtherAppBlocking = false;
        }

        return Task.CompletedTask;
    }
}
