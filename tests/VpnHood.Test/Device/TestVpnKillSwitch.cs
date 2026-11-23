using VpnHood.Core.Client.VpnServices.Abstractions;

namespace VpnHood.Test.Device;

// A kill switch's block in memory, where a test plays a desktop: the rules the OS's firewall would hold.
public class TestVpnKillSwitch : IVpnKillSwitch
{
    private readonly Lock _lock = new();
    private readonly TaskCompletionSource _blockWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // this app's block, while it is up
    public VpnKillSwitchRules? Rules { get; private set; }

    // another app's block, which only the tunnel's holder removes (Unblock's anyApp)
    public bool IsOtherAppBlocking { get; set; }
    public bool IsUp => Rules != null || IsOtherAppBlocking;
    public int BlockCount { get; private set; }

    // every ask for the block, a refused one's too, counted once its outcome is decided: a test that sees the count and
    // then changes BlockException changes the next ask's
    public int BlockCallCount { get; private set; }

    // a block that cannot go up, as an OS's refusal
    public Exception? BlockException { get; set; }

    // an unblock that takes the block down and fails after, as one whose boot copy could not be deleted
    public Exception? UnblockException { get; set; }

    // where a test holds a block as it goes up, as a slow firewall: a block waits for it first
    public Task? BlockGate { get; set; }

    // completes once a block waits at the gate
    public Task BlockWaiting => _blockWaiting.Task;

    // runs as the token of a block held at the gate is canceled, on the canceling thread
    public Action? BlockCanceled { get; set; }

    // runs as a block held at the gate lands, once the gate has let it go, before it takes the rules
    public Action? BlockLanding { get; set; }

    public Task<bool> IsBlocking(CancellationToken cancellationToken)
    {
        return Task.FromResult(Rules != null);
    }

    public async Task Block(VpnKillSwitchRules rules, CancellationToken cancellationToken)
    {
        // the callback registered after the wait's own, so it runs first, as a token runs its callbacks newest first: the
        // wait's end may run on inside its callback, and end this one's registration before its turn
        if (BlockGate is { } blockGate) {
            var wait = blockGate.WaitAsync(cancellationToken);
            await using var registration = cancellationToken.Register(() => BlockCanceled?.Invoke());
            _blockWaiting.TrySetResult();
            await wait;
            BlockLanding?.Invoke();
        }

        lock (_lock) {
            var blockException = BlockException;
            BlockCallCount++;
            if (blockException != null)
                throw blockException;

            Rules = rules;
            IsOtherAppBlocking = false;
            BlockCount++;
        }
    }

    public Task Unblock(bool anyApp, CancellationToken cancellationToken)
    {
        lock (_lock) {
            Rules = null;
            if (anyApp)
                IsOtherAppBlocking = false;

            if (UnblockException != null)
                throw UnblockException;
        }

        return Task.CompletedTask;
    }
}
